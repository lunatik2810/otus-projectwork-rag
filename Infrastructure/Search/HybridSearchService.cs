using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Infrastructure.Data;
using OtusProjectworkRag.Infrastructure.Data.Repositories;
using OtusProjectworkRag.Infrastructure.Embeddings;

namespace OtusProjectworkRag.Infrastructure.Search;

/// <summary>
/// Гибридный поиск: векторный (семантика) + BM25 (точные слова) → RRF.
/// Схема: 50 → 50 → RRF (k=60) → топ-N. Не использует LLM.
/// </summary>
public interface IHybridSearchService
{
    /// <summary>Выполняет гибридный поиск по запросу и возвращает top-K результатов.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IHybridSearchService" />
public sealed class HybridSearchService : IHybridSearchService
{
    private readonly IE5SmallEmbedder _embedder;
    private readonly IVectorSearchService _vectorSearch;
    private readonly IFtsRepository _ftsRepository;
    private readonly IChunkRepository _chunkRepository;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly RetrievalOptions _retrievalOptions;
    private readonly ILogger<HybridSearchService> _logger;

    public HybridSearchService(
        IE5SmallEmbedder embedder,
        IVectorSearchService vectorSearch,
        IFtsRepository ftsRepository,
        IChunkRepository chunkRepository,
        ISqliteConnectionFactory connectionFactory,
        IOptions<RetrievalOptions> retrievalOptions,
        ILogger<HybridSearchService> logger)
    {
        _embedder = embedder;
        _vectorSearch = vectorSearch;
        _ftsRepository = ftsRepository;
        _chunkRepository = chunkRepository;
        _connectionFactory = connectionFactory;
        _retrievalOptions = retrievalOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var finalTopK = topK > 0 ? topK : _retrievalOptions.FinalTopK;

        // 1. Вектор запроса — строго с префиксом "query: ".
        var queryVector = await _embedder.EmbedQueryAsync(query, cancellationToken);

        // 2. Два независимых списка ранжирования.
        var vectorHits = _vectorSearch.Search(queryVector, _retrievalOptions.VectorTopK);

        using var connection = _connectionFactory.CreateConnection();
        var bm25Hits = _ftsRepository.SearchTop(connection, null, query, _retrievalOptions.Bm25TopK);

        _logger.LogDebug(
            "Гибридный поиск '{Query}': векторных {Vector}, BM25 {Bm25}",
            query, vectorHits.Count, bm25Hits.Count);

        // 3. Reciprocal Rank Fusion.
        var fused = RrfFusion.Fuse(vectorHits, bm25Hits, _retrievalOptions.RrfK, finalTopK);
        if (fused.Count == 0)
        {
            return [];
        }

        // 4. Загружаем детали чанков и соединяем с оценками.
        var chunkIds = fused.Select(f => f.ChunkId).ToArray();
        var details = _chunkRepository.GetDetailsByIds(connection, null, chunkIds);
        var detailById = details.ToDictionary(d => d.ChunkId);
        var bm25ById = bm25Hits.ToDictionary(h => h.ChunkId);
        var vectorById = vectorHits.ToDictionary(h => h.ChunkId);

        var result = new List<SearchResult>(fused.Count);
        foreach (var (chunkId, rrfScore) in fused)
        {
            if (!detailById.TryGetValue(chunkId, out var detail))
            {
                continue;
            }

            result.Add(new SearchResult(
                Chunk: detail,
                VectorScore: vectorById.GetValueOrDefault(chunkId)?.Score ?? 0,
                Bm25Score: bm25ById.GetValueOrDefault(chunkId)?.Score ?? 0,
                RrfScore: rrfScore));
        }

        return result;
    }
}