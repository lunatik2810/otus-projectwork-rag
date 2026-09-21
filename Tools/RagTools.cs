using System.ComponentModel;
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Features.Indexing;
using OtusProjectworkRag.Features.Question;
using OtusProjectworkRag.Features.Search;
using OtusProjectworkRag.Features.Status;

namespace OtusProjectworkRag.Tools;

/// <summary>
/// MCP-инструменты сервера RAG. Тонкий слой над MediatR-запросами:
/// транспорт отделён от бизнес-логики (по правилам скила mcp-csharp).
/// Каждый инструмент логируется через Serilog максимально подробно.
/// </summary>
public sealed class RagTools(
    ISender mediator,
    ILogger<RagTools> logger)
{
    /// <summary>
    /// Сканирует папку с документами, разбивает их на фрагменты (чанки),
    /// строит эмбеддинги (multilingual-e5-small) и сохраняет всё в SQLite
    /// вместе с полнотекстовым индексом (FTS5/BM25). Неизменённые файлы
    /// распознаются по SHA-256 хешу и пропускаются; изменённые переиндексируются.
    /// Вызывайте после добавления или изменения документов, а также перед первым поиском.
    /// </summary>
    [McpServerTool]
    [Description(
        "Индексирует папку с документами в базу знаний: разбивает файлы на фрагменты, " +
        "строит векторные эмбеддинги (multilingual-e5-small, 384 измерения) и кладёт " +
        "текст, векторы и полнотекстовый индекс в SQLite (vectorDb). Файлы без изменений " +
        "(SHA-256) пропускаются, изменённые переиндексируются. Вызывайте до первого " +
        "поиска и после добавления/изменения документов в папке.")]
    public async Task<IndexFolderResult> IndexFolder(
        [Description("Полный путь к папке с документами")] string folderPath,
        [Description("Glob-паттерн для выбора файлов, например *.txt, *.md (по умолчанию *.txt)")]
        string globPattern = "*.txt",
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "MCP-инструмент index_folder: папка '{Folder}', паттерн '{Glob}'",
            folderPath, globPattern);

        var stopwatch = Stopwatch.StartNew();
        var result = await mediator.Send(
            new IndexFolderRequest(folderPath, globPattern), cancellationToken);
        stopwatch.Stop();

        logger.LogInformation(
            "MCP-инструмент index_folder завершён за {Ms:F0} мс: " +
            "сканировано {Scanned}, добавлено {Added}, обновлено {Updated}, " +
            "без изменений {Unchanged}, ошибок {Failed}, чанков {Chunks}",
            stopwatch.Elapsed.TotalMilliseconds, result.ScannedFiles, result.Added,
            result.Updated, result.Unchanged, result.Failed, result.Chunks);

        return result;
    }

    /// <summary>
    /// Запускает полный Corrective RAG-цикл: гибридный поиск (BM25 + вектор → RRF),
    /// оценка релевантности найденных фрагментов локальной LLM (Ollama).
    /// Если релевантных фрагментов недостаточно — запрос расширяется и поиск
    /// повторяется (до 2 раз). Возвращает проверенные фрагменты с метаданными.
    /// </summary>
    [McpServerTool]
    [Description(
        "Отвечает на вопрос по содержимому базы знаний: выполняет гибридный поиск " +
        "(BM25 + векторный → RRF), затем локальная LLM (Ollama) оценивает релевантность " +
        "каждого фрагмента; если релевантных мало — запрос расширяется и поиск " +
        "повторяется (до 2 раз). В ответе — отобранные фрагменты с метаданными " +
        "(файл, id чанка, оценки близости); финальный ответ сформулируйте сами " +
        "на основе этих фрагментов.")]
    public async Task<AskQuestionResult> AskQuestion(
        [Description("Вопрос к базе знаний на естественном языке")] string question,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MCP-инструмент ask_question: '{Question}'", question);

        var stopwatch = Stopwatch.StartNew();
        var result = await mediator.Send(new AskQuestionQuery(question), cancellationToken);
        stopwatch.Stop();

        logger.LogInformation(
            "MCP-инструмент ask_question завершён за {Ms:F0} мс: попыток {Attempts}, " +
            "расширений {Expansions}, результатов {Results}",
            stopwatch.Elapsed.TotalMilliseconds, result.Attempts,
            result.ExpandedQueries.Count, result.Results.Count);

        return result;
    }

    /// <summary>
    /// Возвращает ранжированные фрагменты документов без генерации ответа и без
    /// использования LLM (гибридный поиск: BM25 + векторный → RRF).
    /// </summary>
    [McpServerTool]
    [Description(
        "Ищет релевантные фрагменты документов по запросу: гибридный поиск " +
        "(BM25 по точным словам + векторная близость через эмбеддинги, затем слияние " +
        "Reciprocal Rank Fusion). Не использует LLM и не генерирует ответ — только " +
        "ранжированные фрагменты с метаданными (имя файла, путь, id чанка, оценки " +
        "близости). Полезен, когда нужно быстро получить источники по запросу.")]
    public async Task<FindRelevantDocsResult> FindRelevantDocs(
        [Description("Поисковый запрос на естественном языке")] string query,
        [Description("Сколько фрагментов вернуть (по умолчанию 10)")] int topK = 10,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "MCP-инструмент find_relevant_docs: запрос '{Query}', topK={TopK}",
            query, topK);

        var stopwatch = Stopwatch.StartNew();
        var results = await mediator.Send(
            new FindRelevantDocsQuery(query, topK), cancellationToken);
        stopwatch.Stop();

        logger.LogInformation(
            "MCP-инструмент find_relevant_docs завершён за {Ms:F0} мс: результатов {Count}",
            stopwatch.Elapsed.TotalMilliseconds, results.Count);

        return new FindRelevantDocsResult(query, topK, results);
    }

    /// <summary>
    /// Возвращает статистику базы знаний: число документов, фрагментов и векторов,
    /// время последней индексации, путь к файлу базы данных. Без LLM.
    /// </summary>
    [McpServerTool]
    [Description(
        "Возвращает статистику базы знаний: количество проиндексированных документов, " +
        "фрагментов и векторов, дату последней индексации и путь к файлу базы данных. " +
        "Используйте, чтобы проверить, что документы уже проиндексированы, и понять, " +
        "насколько актуальны данные.")]
    public async Task<IndexStatusResult> IndexStatus(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MCP-инструмент index_status: запрос статистики");

        var result = await mediator.Send(new IndexStatusQuery(), cancellationToken);

        logger.LogInformation(
            "MCP-инструмент index_status: документов {Documents}, чанков {Chunks}, " +
            "векторов {Embeddings}, последняя индексация {LastIndexed}",
            result.DocumentCount, result.ChunkCount, result.EmbeddingCount,
            result.LastIndexedAt?.ToString("O") ?? "нет");

        return result;
    }
}