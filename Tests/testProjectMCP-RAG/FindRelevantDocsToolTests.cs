using Microsoft.Extensions.DependencyInjection;
using OtusProjectworkRag.Infrastructure.Data;
using OtusProjectworkRag.Infrastructure.Data.Repositories;
using OtusProjectworkRag.Infrastructure.Embeddings;
using OtusProjectworkRag.Infrastructure.Search;
using OtusProjectworkRag.Tools;
using Xunit;

namespace testProjectMCP_RAG;

/// <summary>
/// Тесты MCP-инструмента find_relevant_docs ([McpServerTool] <see cref="RagTools.FindRelevantDocs"/>)
/// БЕЗ запуска MCP-сервера, по аналогии с IndexFolderToolTests/IndexStatusToolTests.
/// Проверяется вся реальная цепочка: инструмент → MediatR → FindRelevantDocsQueryHandler →
/// HybridSearchService (BM25 + векторный поиск → RRF) → SQLite.
///
/// Документы — копии досье из Resources/DataBaseRAG (5 .txt + notes.md вне выборки).
/// Ключевые токены проверены по исходникам:
///   «Миронов»   — только dossier_001.txt («Миронова» в dossier_003 — другой токен);
///   «Казань»    — только dossier_002.txt;
///   «инженер по автоматизации» — dossier_001.txt и dossier_002.txt;
///   «финансовые» — во всех пяти досье.
/// </summary>
public class FindRelevantDocsToolTests
{
    private const string FirstDossier = "dossier_001.txt";
    private const string SecondDossier = "dossier_002.txt";

    /// <summary>
    /// Общий префикс: свежий хост + индексация ВСЕХ .txt-досье из папки DocumentsDir.
    /// Возвращает хост; вызывающий тест оформляет его через using.
    /// </summary>
    private static async Task<RagTestHost> CreateIndexedHostAsync()
    {
        var host = new RagTestHost();
        var tools = host.Services.GetRequiredService<RagTools>();

        var indexed = await tools.IndexFolder(
            host.DocumentsDir, "*.txt", TestContext.Current.CancellationToken);

        Assert.Equal(host.DocCount, indexed.ScannedFiles);
        Assert.Equal(host.DocCount, indexed.Added);
        Assert.Equal(0, indexed.Failed);

        return host;
    }

    /// <summary>
    /// Точное слово «Миронов» встречается только в dossier_001.txt (в dossier_003 —
    /// форма «Миронова», для FTS5 это другой токен). Верхний результат по RRF
    /// должен быть чанком именно этого файла.
    /// </summary>
    [Fact]
    public async Task FindRelevantDocs_ExactWord_TopResultFromExpectedDossier()
    {
        using var host = await CreateIndexedHostAsync();
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.FindRelevantDocs(
            "Миронов", topK: 5, TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Results);
        Assert.Equal(FirstDossier, result.Results[0].Chunk.FileName);
    }

    /// <summary>
    /// Уникальное слово «Казань» есть только в dossier_002.txt. Векторная часть
    /// гибридного поиска вернёт и чужие чанки, поэтому строго «все результаты из
    /// одного файла» не проверяем — только верхний результат и наличие нужного файла.
    /// </summary>
    [Fact]
    public async Task FindRelevantDocs_UniqueTerm_TopResultFromExpectedDossier()
    {
        using var host = await CreateIndexedHostAsync();
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.FindRelevantDocs(
            "Казань", topK: 5, TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Results);
        Assert.Equal(SecondDossier, result.Results[0].Chunk.FileName);
        Assert.Contains(result.Results, r => r.Chunk.FileName == SecondDossier);
    }

    /// <summary>
    /// topK ограничивает число результатов: «инженер по автоматизации» есть ровно
    /// в двух досье, и с topK=2 инструмент возвращает ровно два чанка,
    /// отсортированных по RRF по убыванию.
    /// </summary>
    [Fact]
    public async Task FindRelevantDocs_TopK_LimitsResultCountAndKeepsOrder()
    {
        using var host = await CreateIndexedHostAsync();
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.FindRelevantDocs(
            "инженер по автоматизации", topK: 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TopK);
        Assert.Equal(2, result.Results.Count);

        // Результаты упорядочены по RRF от лучшего к худшему.
        for (var i = 1; i < result.Results.Count; i++)
        {
            Assert.True(
                result.Results[i - 1].RrfScore >= result.Results[i].RrfScore,
                "Результаты должны быть отсортированы по RrfScore по убыванию.");
        }
    }

    /// <summary>
    /// Метаданные и оценки заполнены: чанк связан с документом (путь внутри
    /// DocumentsDir), текст и число токенов непустые, RRF-оценка положительная.
    /// </summary>
    [Fact]
    public async Task FindRelevantDocs_MetadataAndScores_AreFilled()
    {
        using var host = await CreateIndexedHostAsync();
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.FindRelevantDocs(
            "финансовые", topK: 5, TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Results);
        foreach (var item in result.Results)
        {
            Assert.True(item.Chunk.ChunkId > 0, "ChunkId должен быть положительным.");
            Assert.True(item.Chunk.TokenCount > 0, "TokenCount должен быть положительным.");
            Assert.False(string.IsNullOrWhiteSpace(item.Chunk.Text), "Текст чанка не может быть пустым.");
            Assert.False(string.IsNullOrWhiteSpace(item.Chunk.FileName), "FileName не может быть пустым.");
            Assert.StartsWith(host.DocumentsDir, item.Chunk.SourcePath);
            Assert.True(item.RrfScore > 0, "RRF-оценка найденного чанка должна быть положительной.");
        }
    }

    /// <summary>
    /// Пустая база знаний: поиск не падает и возвращает пустой список.
    /// </summary>
    [Fact]
    public async Task FindRelevantDocs_EmptyDatabase_ReturnsEmptyList()
    {
        using var host = new RagTestHost();
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.FindRelevantDocs(
            "Миронов", topK: 10, TestContext.Current.CancellationToken);

        Assert.Empty(result.Results);
    }

    /// <summary>
    /// Пробельный запрос: ранний выход в HybridSearchService (IsNullOrWhiteSpace) —
    /// пустой результат без обращения к модели и БД.
    /// </summary>
    [Fact]
    public async Task FindRelevantDocs_BlankQuery_ReturnsEmptyList()
    {
        using var host = await CreateIndexedHostAsync();
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.FindRelevantDocs(
            "   ", topK: 10, TestContext.Current.CancellationToken);

        Assert.Empty(result.Results);
    }

    /// <summary>
    /// Каскад кандидатов: до обрезки хотя бы один метод поиска (BM25 или векторный)
    /// находит БОЛЬШЕ 12 чанков, а инструмент с дефолтным topK=10 возвращает не больше
    /// 10 — то есть RRF действительно что-то обрезает.
    ///
    /// Число кандидатов смотрим напрямую через репозиторий FTS и векторный поиск
    /// (эти списки в публичный результат не попадают).
    /// </summary>
    [Fact]
    public async Task FindRelevantDocs_CandidatePipeline_HasMoreThanFinalTopKThenTruncates()
    {
        using var host = await CreateIndexedHostAsync();
        var tools = host.Services.GetRequiredService<RagTools>();

        const string query = "финансовые";

        // Кандидаты ДО RRF: BM25 и векторный поиск с заведомо большим topK.
        var connectionFactory = host.Services.GetRequiredService<ISqliteConnectionFactory>();
        var ftsRepository = host.Services.GetRequiredService<IFtsRepository>();
        var embedder = host.Services.GetRequiredService<IE5SmallEmbedder>();
        var vectorSearch = host.Services.GetRequiredService<IVectorSearchService>();

        using var connection = connectionFactory.CreateConnection();
        var bm25Candidates = ftsRepository.SearchTop(connection, null, query, topK: 100);

        var queryVector = await embedder.EmbedQueryAsync(query, TestContext.Current.CancellationToken);
        var vectorCandidates = vectorSearch.Search(queryVector, topK: 100);

        // Хотя бы один метод должен дать больше 12 кандидатов, иначе обрезать нечего.
        var maxCandidates = Math.Max(bm25Candidates.Count, vectorCandidates.Count);
        Assert.True(
            maxCandidates > 12,
            $"Ожидалось больше 12 кандидатов до RRF, получено: BM25={bm25Candidates.Count}, векторных={vectorCandidates.Count}");

        // Инструмент с дефолтным topK=10 (FinalTopK из конфигурации) возвращает ≤ 10.
        var result = await tools.FindRelevantDocs(
            query, topK: 10, TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Results);
        Assert.True(
            result.Results.Count <= 10,
            $"RRF должен обрезать кандидатов до 10, получено {result.Results.Count}");
    }
}