using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;
using OtusProjectworkRag.Infrastructure.Data;
using OtusProjectworkRag.Infrastructure.Data.Repositories;
using OtusProjectworkRag.Infrastructure.Embeddings;
using OtusProjectworkRag.Infrastructure.Indexing;
using OtusProjectworkRag.Infrastructure.Text;
using OtusProjectworkRag.Tools;

namespace testProjectMCP_RAG;

/// <summary>
/// Тестовый хост для проверки цепочки индексации БЕЗ MCP-сервера.
/// Собирает DI-контейнер (зеркало Program.cs: только части, нужные для индексации),
/// создаёт временную папку с документами и отдельную временную базу данных —
/// рабочая Resources/vectorDb.db не затрагивается.
///
/// Каждый тест создаёт СВОЙ экземпляр хоста: свежая папка и свежая БД,
/// поэтому порядок выполнения тестов не влияет на счётчики Added/Unchanged.
/// </summary>
public sealed class RagTestHost : IDisposable
{
    // Корень временных файлов — внутри каталога тестового проекта,
    // а не %TEMP%/AppData: так удобнее контролировать и очищать (замечание пользователя).
    private static readonly string TempRoot =
        Path.Combine(ResolveTestProjectDirectory(), ".rag-test-tmp");

    /// <summary>Папка с тестовыми документами.</summary>
    public string DocumentsDir { get; }

    /// <summary>Число .txt-файлов в DocumentsDir (в папке есть ещё один .md — вне выборки).</summary>
    public int DocCount { get; }

    /// <summary>DI-контейнер с реальными сервисами приложения.</summary>
    public ServiceProvider Services { get; }

    private readonly string _sessionDir;
    private readonly string _databasePath;

    public RagTestHost()
    {
        // Кодировки Windows-1251 недоступны по умолчанию (см. Program.cs) —
        // регистрируем провайдер, иначе чтение русскоязычных txt-файлов падает.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Уникальная сессионная папка под временные файлы этого экземпляра хоста:
        // свои документы и свою базу данных тест создаёт здесь и удаляет в Dispose.
        _sessionDir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sessionDir);

        // Временная папка с документами: 3 .txt (попадают в выборку) + 1 .md (игнорируется).
        DocumentsDir = Path.Combine(_sessionDir, "docs");
        Directory.CreateDirectory(DocumentsDir);
        File.WriteAllText(Path.Combine(DocumentsDir, "doc_001.txt"),
            "Иванов Иван Иванович, 1985 года рождения. " +
            "Работает в отделе разработки с 2010 года. " +
            "Основные обязанности: проектирование архитектуры, код-ревью, наставничество.");
        File.WriteAllText(Path.Combine(DocumentsDir, "doc_002.txt"),
            "Петрова Мария Сергеевна, 1990 года рождения. " +
            "Менеджер проектов. Курирует команду из шести человек. " +
            "Сертификаты: PMP, Agile Coach.");
        File.WriteAllText(Path.Combine(DocumentsDir, "doc_003.txt"),
            "Сидоров Пётр Алексеевич, 1978 года рождения. " +
            "Системный администратор. Отвечает за инфраструктуру и безопасность. " +
            "Стаж в компании — 12 лет.");
        File.WriteAllText(Path.Combine(DocumentsDir, "notes.md"),
            "# Это Markdown-файл: при паттерне *.txt он не должен попасть в индексацию.");
        DocCount = 3;

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

        // === Логирование и MediatR. ===
        // typeof(Program) из top-level statements внутренний и из тестовой сборки
        // недоступен — регистрируем обработчики по сборке RagTools.
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
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