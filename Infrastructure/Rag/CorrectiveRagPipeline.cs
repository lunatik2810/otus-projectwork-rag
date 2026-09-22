using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Infrastructure.Search;

namespace OtusProjectworkRag.Infrastructure.Rag;

/// <summary>
/// Corrective RAG-пайплайн:
///
///   User Query → Retrieve (BM25 + Vector → RRF) → Grade Chunks (Ollama yes/no)
///     → достаточно релевантных → вернуть чанки агенту
///     → слишком мало → расширить запрос через Ollama → повторный поиск (до 2 раз)
///
/// Финальный ответ НЕ генерируется — отобранные чанки возвращаются агенту-хосту.
/// </summary>
public interface ICorrectiveRagPipeline
{
    /// <summary>Выполняет полный цикл поиска с грейдингом и расширением запроса.</summary>
    Task<AskQuestionResult> AskAsync(string question, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ICorrectiveRagPipeline" />
public sealed class CorrectiveRagPipeline : ICorrectiveRagPipeline
{
    private readonly IHybridSearchService _search;
    private readonly IChunkGrader _grader;
    private readonly IQueryExpander _expander;
    private readonly RetrievalOptions _options;
    private readonly ILogger<CorrectiveRagPipeline> _logger;

    public CorrectiveRagPipeline(
        IHybridSearchService search,
        IChunkGrader grader,
        IQueryExpander expander,
        IOptions<RetrievalOptions> options,
        ILogger<CorrectiveRagPipeline> logger)
    {
        _search = search;
        _grader = grader;
        _expander = expander;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AskQuestionResult> AskAsync(string question, CancellationToken cancellationToken)
    {
        var query = question;
        var expandedQueries = new List<string>();
        var attempts = 1;
        var results = await _search.SearchAsync(query, _options.FinalTopK, cancellationToken);

        // Первая попытка — исходный запрос; затем до MaxExpansions расширений.
        for (var expansion = 0; expansion <= _options.MaxExpansions; expansion++)
        {
            var graded = await _grader.GradeAsync(question, results, cancellationToken);
            var relevantCount = graded.Count(r => r.Relevant);

            _logger.LogInformation(
                "Попытка {Attempt}/{Max}: релевантных чанков {Relevant} из {Total} (нужно >= {Min})",
                attempts, _options.MaxExpansions + 1, relevantCount, graded.Count, _options.MinRelevant);

            // Достаточно релевантных (или исчерпаны расширения) — отдаём результат агенту.
            if (relevantCount >= _options.MinRelevant || expansion == _options.MaxExpansions)
            {
                return new AskQuestionResult(question, attempts, expandedQueries, graded);
            }

            _logger.LogInformation(
               "Попытка расширения поиска {Attempt}/{Max}", attempts, _options.MaxExpansions);
            // Расширяем запрос и повторяем поиск.
            query = await _expander.ExpandAsync(question, cancellationToken);
            expandedQueries.Add(query);
            attempts++;

            results = await _search.SearchAsync(query, _options.FinalTopK, cancellationToken);
        }

        // Теоретически недостижимо (цикл всегда возвращает на последней итерации).
        return new AskQuestionResult(question, attempts, expandedQueries, []);
    }
}