using System.Text;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;
using OtusProjectworkRag.Infrastructure;
using OtusProjectworkRag.Infrastructure.Data;
using OtusProjectworkRag.Infrastructure.Data.Repositories;
using OtusProjectworkRag.Infrastructure.Embeddings;
using OtusProjectworkRag.Infrastructure.Indexing;
using OtusProjectworkRag.Infrastructure.Ollama;
using OtusProjectworkRag.Infrastructure.Rag;
using OtusProjectworkRag.Infrastructure.Search;
using OtusProjectworkRag.Infrastructure.Text;
using OtusProjectworkRag.Tools;
using Serilog;

// Кодировка Windows-1251 недоступна по умолчанию в .NET Core и новее.
// Регистрация провайдера обязательна для fallback при чтении русскоязычных
// txt-файлов, в том числе на Linux (в Docker-контейнере).
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Bootstrap-логгер: виден сразу, до полной настройки Serilog из конфигурации.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // === Логирование: Serilog пишет ОДНОВРЕМЕННО в stdout и в файл, уровень — Information. ===
    // Уровень переопределяется через env Serilog__MinimumLevel__Default,
    // путь к файлу логов — через Serilog__LogFile.
    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                context.Configuration["Serilog:LogFile"] ?? Path.Combine("logs", "rag-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14));

    // === Конфигурация: все секции читаются из appsettings.json и могут быть ===
    // === переопределены переменными окружения (Section__Key). ===
    builder.Services.AddOptions();
    builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
    builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection(EmbeddingOptions.SectionName));
    builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection(ChunkingOptions.SectionName));
    builder.Services.Configure<IndexingOptions>(builder.Configuration.GetSection(IndexingOptions.SectionName));
    builder.Services.Configure<RetrievalOptions>(builder.Configuration.GetSection(RetrievalOptions.SectionName));
    builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));

    // === Модель эмбеддингов и токенизатор (singleton: загружаются в память один раз). ===
    builder.Services.AddSingleton<ITokenizerProvider, TokenizerProvider>();
    builder.Services.AddSingleton<IE5SmallEmbedder, E5SmallEmbedder>();

    // === Слой данных. ===
    // Фабрика соединений создаёт каталог базы и включает WAL/внешние ключи.
    builder.Services.AddSingleton<ISqliteConnectionFactory>(serviceProvider =>
    {
        var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        return new SqliteConnectionFactory(AppPaths.ResolveDatabasePath(databaseOptions.Path));
    });

    builder.Services.AddSingleton<IDocumentRepository, DocumentRepository>();
    builder.Services.AddSingleton<IChunkRepository, ChunkRepository>();
    builder.Services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();
    builder.Services.AddSingleton<IFtsRepository, FtsRepository>();

    // === Текст, чанкинг, индексация. ===
    // ITokenCounter реализуется тем же синглтоном, что и токенизатор,
    // чтобы токенизатор существовал в одном экземпляре.
    builder.Services.AddSingleton<ITokenCounter>(serviceProvider =>
        serviceProvider.GetRequiredService<ITokenizerProvider>());
    builder.Services.AddSingleton<ITextChunker, TextChunker>();
    builder.Services.AddSingleton<IFileScanner, FileScanner>();
    builder.Services.AddSingleton<IIndexerService, IndexerService>();

    // === Поиск (векторный + BM25 → RRF). ===
    builder.Services.AddSingleton<IVectorSearchService, VectorSearchService>();
    builder.Services.AddSingleton<IHybridSearchService, HybridSearchService>();

    // === Ollama (локальная LLM) и Corrective RAG. ===
    // Typed HttpClient: адрес и таймаут задаются в OllamaClient из конфигурации.
    builder.Services.AddHttpClient<OllamaClient>();
    builder.Services.AddSingleton<IOllamaClient>(serviceProvider =>
        serviceProvider.GetRequiredService<OllamaClient>());
    builder.Services.AddSingleton<IChunkGrader, ChunkGrader>();
    builder.Services.AddSingleton<IQueryExpander, QueryExpander>();
    builder.Services.AddSingleton<ICorrectiveRagPipeline, CorrectiveRagPipeline>();

    // === MediatR: регистрируем все Request/Handler из текущей сборки. ===
    builder.Services.AddMediatR(configuration =>
        configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));

    // === MCP-сервер: HTTP-транспорт (Streamable), инструменты RAG. ===
    builder.Services
        .AddMcpServer()
        .WithHttpTransport(options =>
        {
            // Stateless — серверу не нужны server-to-client запросы (sampling и т.п.).
            options.Stateless = true;
        })
        .WithTools<RagTools>();

    var app = builder.Build();

    // === Инициализация схемы БД при старте (идемпотентно). ===
    using (var scope = app.Services.CreateScope())
    {
        var connectionFactory = scope.ServiceProvider.GetRequiredService<ISqliteConnectionFactory>();
        using var connection = connectionFactory.CreateConnection();
        SchemaInitializer.EnsureCreated(connection);
        Log.Information("Схема базы данных готова: {Path}", connectionFactory.DatabasePath);
    }

    // === Health-эндпоинт для healthcheck в docker-compose (curl /health). ===
    app.MapGet("/health", () => Results.Ok("ok"));

    app.MapMcp();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Не удалось запустить MCP-сервер");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
