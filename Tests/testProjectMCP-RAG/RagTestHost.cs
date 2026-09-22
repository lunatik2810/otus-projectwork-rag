using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;
using Serilog;
using OtusProjectworkRag.Infrastructure.Data;
using OtusProjectworkRag.Infrastructure.Data.Repositories;
using OtusProjectworkRag.Infrastructure.Embeddings;
using OtusProjectworkRag.Infrastructure.Indexing;
using OtusProjectworkRag.Infrastructure.Ollama;
using OtusProjectworkRag.Infrastructure.Rag;
using OtusProjectworkRag.Infrastructure.Search;
using OtusProjectworkRag.Infrastructure.Text;
using OtusProjectworkRag.Tools;

namespace testProjectMCP_RAG;

/// <summary>
/// Тестовый хост для проверки цепочек индексации, поиска и Corrective RAG
/// БЕЗ MCP-сервера. Собирает DI-контейнер (зеркало Program.cs), копирует документы
/// из Resources/DataBaseRAG тестового проекта во временную папку и создаёт отдельную
/// временную базу данных — рабочая Resources/vectorDb.db не затрагивается.
///
/// Каждый тест создаёт СВОЙ экземпляр хоста: свежая папка и свежая БД,
/// поэтому порядок выполнения тестов не влияет на счётчики Added/Unchanged.
/// Для ask_question можно передать заглушку IOllamaClient (детерминированные тесты),
/// по умолчанию регистрируется реальный OllamaClient.
/// </summary>
public sealed class RagTestHost : IDisposable
{
    // Корень временных файлов — внутри каталога тестового проекта,
    // а не %TEMP%/AppData: так удобнее контролировать и очищать (замечание пользователя).
    private static readonly string TempRoot =
        Path.Combine(ResolveTestProjectDirectory(), ".rag-test-tmp");

    // === Логирование: Serilog (консоль + файл), как в Program.cs, но для тестов. ===
    // Глобальный Log.Logger инициализируем ОДИН раз на процесс под lock:
    // xunit.v3 запускает тест-классы параллельно, а Log.Logger — статический синглтон.
    // Файл логов лежит в папке logs каталога тестового проекта
    // (Tests/testProjectMCP-RAG/logs) и переживает очистку временных сессий.
    private static readonly object SerilogSync = new();
    private static bool _serilogConfigured;

    /// <summary>Папка с тестовыми документами.</summary>
    public string DocumentsDir { get; }

    /// <summary>Число .txt-файлов в DocumentsDir (в папке есть ещё один .md — вне выборки).</summary>
    public int DocCount { get; }

    /// <summary>DI-контейнер с реальными сервисами приложения.</summary>
    public ServiceProvider Services { get; }

    private readonly string _sessionDir;
    private readonly string _databasePath;

    /// <summary>
    /// Создаёт тестовый хост. По умолчанию регистрируется реальный OllamaClient
    /// (зеркало Program.cs); тесты ask_question могут передать заглушку
    /// <paramref name="ollamaOverride"/> — детерминированные ответы без сети.
    /// </summary>
    public RagTestHost(IOllamaClient? ollamaOverride = null)
    {
        // Кодировки Windows-1251 недоступны по умолчанию (см. Program.cs) —
        // регистрируем провайдер, иначе чтение русскоязычных txt-файлов падает.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Уникальная сессионная папка под временные файлы этого экземпляра хоста:
        // свои документы и свою базу данных тест создаёт здесь и удаляет в Dispose.
        _sessionDir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sessionDir);

        // Временная папка с документами: копируем ВСЕ файлы из Resources/DataBaseRAG
        // ТЕСТОВОГО ПРОЕКТА (исходное дерево, а не вывод сборки: в bin/.../Resources
        // попадают ещё и 35 досье основного проекта через ProjectReference, и корпус
        // теста непредсказуемо смешивается). Тест работает со своей копией — исходные
        // файлы не затрагиваются. notes.md остаётся вне выборки при паттерне *.txt
        // и нужен для проверки glob-фильтра.
        DocumentsDir = Path.Combine(_sessionDir, "docs");
        Directory.CreateDirectory(DocumentsDir);
        var sourceDocs = Path.Combine(ResolveTestProjectDirectory(), "Resources", "DataBaseRAG");
        foreach (var file in Directory.EnumerateFiles(sourceDocs))
        {
            File.Copy(file, Path.Combine(DocumentsDir, Path.GetFileName(file)));
        }

        // Число .txt-файлов в DocumentsDir (notes.md — вне выборки по умолчанию).
        DocCount = Directory.EnumerateFiles(DocumentsDir, "*.txt").Count();

        // Отдельная временная БД на каждый тест (рядом с документами, в папке проекта).
        _databasePath = Path.Combine(_sessionDir, "vectorDb-test.db");

        var services = new ServiceCollection();

        // === Конфигурация: значения из appsettings.json. ===
        // Свойства опций объявлены как init-only — присваивать их в лямбде
        // Configure<T>(Action<T>) нельзя, регистрируем готовые экземпляры через Options.Create.
        services.AddSingleton<IOptions<DatabaseOptions>>(
            Options.Create(new DatabaseOptions { Path = _databasePath }));
        services.AddSingleton<IOptions<EmbeddingOptions>>(
            Options.Create(new EmbeddingOptions
            {
                ModelDirectory = "Resources/multilingual-e5-small",
                ModelFileName = "model_O4.onnx",
                TokenizerFileName = "tokenizer.json",
                BatchSize = 16,
                MaxSequenceLength = 512,
                Dimensions = 384,
            }));
        services.AddSingleton<IOptions<ChunkingOptions>>(
            Options.Create(new ChunkingOptions
            {
                MaxTokens = 470,
                OverlapTokens = 20,
            }));
        services.AddSingleton<IOptions<IndexingOptions>>(
            Options.Create(new IndexingOptions { DefaultGlob = "*.txt" }));

        // === Модель эмбеддингов и токенизатор (singleton, как в Program.cs). ===
        services.AddSingleton<ITokenizerProvider, TokenizerProvider>();
        services.AddSingleton<IE5SmallEmbedder, E5SmallEmbedder>();

        // === Слой данных. ===
        services.AddSingleton<ISqliteConnectionFactory>(_ => new SqliteConnectionFactory(_databasePath));
        services.AddSingleton<IDocumentRepository, DocumentRepository>();
        services.AddSingleton<IChunkRepository, ChunkRepository>();
        services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();
        services.AddSingleton<IFtsRepository, FtsRepository>();

        // === Текст, чанкинг, индексация. ===
        services.AddSingleton<ITokenCounter>(sp => sp.GetRequiredService<ITokenizerProvider>());
        services.AddSingleton<ITextChunker, TextChunker>();
        services.AddSingleton<IFileScanner, FileScanner>();
        services.AddSingleton<IIndexerService, IndexerService>();

        // === Поиск (векторный + BM25 → RRF), нужен для инструментов find_relevant_docs и ask_question. ===
        services.AddSingleton<IVectorSearchService, VectorSearchService>();
        services.AddSingleton<IHybridSearchService, HybridSearchService>();

        // === Ollama (локальная LLM) и Corrective RAG — зеркало Program.cs. ===
        // Опции берутся с дефолтными значениями (как остальные опции хоста);
        // реальные значения из appsettings.json здесь не читаются.
        services.AddSingleton<IOptions<RetrievalOptions>>(
            Options.Create(new RetrievalOptions()));
        services.AddSingleton<IOptions<OllamaOptions>>(
            Options.Create(new OllamaOptions()));

        // По умолчанию — реальный клиент (typed HttpClient, как в Program.cs).
        // Тесты ask_question передают заглушку IOllamaClient: регистрации ленивые,
        // поэтому в тестах индексации/поиска сеть не затрагивается вовсе.
        if (ollamaOverride is null)
        {
            services.AddHttpClient<OllamaClient>();
            services.AddSingleton<IOllamaClient>(sp => sp.GetRequiredService<OllamaClient>());
        }
        else
        {
            services.AddSingleton<IOllamaClient>(ollamaOverride);
        }

        services.AddSingleton<IChunkGrader, ChunkGrader>();
        services.AddSingleton<IQueryExpander, QueryExpander>();
        services.AddSingleton<ICorrectiveRagPipeline, CorrectiveRagPipeline>();

        // === Логирование и MediatR. ===
        // typeof(Program) из top-level statements внутренний и из тестовой сборки
        // недоступен — регистрируем обработчики по сборке RagTools.
        // Serilog инициализируем один раз на процесс; контейнеру логгер НЕ передаётся
        // во владение (dispose: false), чтобы Dispose одного хоста не закрывал
        // общий Log.Logger, пока его используют другие параллельные тесты.
        EnsureSerilogConfigured();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSerilog(dispose: false);
        });
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(RagTools).Assembly));

        // === Сам инструмент: конструктор (ISender mediator, ILogger<RagTools>). ===
        services.AddSingleton<RagTools>();

        Services = services.BuildServiceProvider();

        // Инициализация схемы БД при старте (идемпотентно), как в Program.cs.
        var connectionFactory = Services.GetRequiredService<ISqliteConnectionFactory>();
        using var connection = connectionFactory.CreateConnection();
        SchemaInitializer.EnsureCreated(connection);
    }

    public void Dispose()
    {
        Services.Dispose();

        // Microsoft.Data.Sqlite пулирует соединения (Pooling включён по умолчанию):
        // без очистки пула файл БД (и -wal/-shm) остаётся заблокированным до выхода
        // из процесса, и удалить сессионную папку невозможно.
        SqliteConnection.ClearAllPools();

        // Удаляем только сессионную папку ЭТОГО хоста: параллельные тесты
        // используют собственные подпапки и не затрагиваются.
        // SQLite (WAL) может кратковременно держать файлы — повторяем с паузой,
        // предварительно снимая атрибуты (файл мог стать read-only).
        for (var attempt = 0; attempt < 3 && Directory.Exists(_sessionDir); attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             _sessionDir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_sessionDir, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }

        // Если корневая папка временных файлов опустела — удаляем и её.
        try
        {
            if (Directory.Exists(TempRoot)
                && !Directory.EnumerateFileSystemEntries(TempRoot).Any())
            {
                Directory.Delete(TempRoot);
            }
        }
        catch (IOException)
        {
            // Игнорируем: другой параллельный тест мог только что создать свою сессию.
        }
    }

    /// <summary>
    /// Настраивает глобальный Serilog-логгер тестов: консоль + файл test-rag-*.log
    /// в папке logs каталога тестового проекта (по аналогии с Program.cs,
    /// уровень Information). Вызывается при каждом создании хоста, но реальная
    /// инициализация выполняется только один раз на процесс (защита от
    /// параллельных тестов).
    /// </summary>
    private static void EnsureSerilogConfigured()
    {
        if (_serilogConfigured)
        {
            return;
        }

        lock (SerilogSync)
        {
            if (_serilogConfigured)
            {
                return;
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(ResolveTestProjectDirectory(), "logs", "test-rag-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14)
                .CreateLogger();

            _serilogConfigured = true;
        }
    }

    /// <summary>
    /// Каталог тестового проекта. AppContext.BaseDirectory указывает на
    /// ...\Tests\testProjectMCP-RAG\bin\Debug\net10.0\ — поднимаемся на три уровня.
    /// </summary>
    private static string ResolveTestProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var projectDirectory = directory.Parent?.Parent?.Parent?.FullName;
        return projectDirectory
            ?? throw new InvalidOperationException(
                "Не удалось определить каталог тестового проекта по пути сборки.");
    }
}