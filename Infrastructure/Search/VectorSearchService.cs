using Microsoft.Extensions.Logging;
using OtusProjectworkRag.Infrastructure.Data;
using OtusProjectworkRag.Infrastructure.Data.Repositories;

namespace OtusProjectworkRag.Infrastructure.Search;

/// <summary>Результат ранжирования: идентификатор чанка и оценка близости.</summary>
public sealed record SearchHit(int ChunkId, double Score);

/// <summary>
/// Векторный поиск полным перебором (brute force). Для учебного объёма
/// (~сотни чанков по 384 измерения) вычисление скалярных произведений мгновенно,
/// ANN-индекс не требуется. Векторы L2-нормализованы при записи, поэтому
/// косинусная близость сводится к скалярному произведению.
/// </summary>
public interface IVectorSearchService
{
    /// <summary>Возвращает top-K чанков по близости к вектору запроса.</summary>
    IReadOnlyList<SearchHit> Search(float[] queryVector, int topK);
}

/// <inheritdoc cref="IVectorSearchService" />
public sealed class VectorSearchService : IVectorSearchService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly ILogger<VectorSearchService> _logger;

    public VectorSearchService(
        ISqliteConnectionFactory connectionFactory,
        IEmbeddingRepository embeddingRepository,
        ILogger<VectorSearchService> logger)
    {
        _connectionFactory = connectionFactory;
        _embeddingRepository = embeddingRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchHit> Search(float[] queryVector, int topK)
    {
        using var connection = _connectionFactory.CreateConnection();
        var records = _embeddingRepository.GetAll(connection, null);

        var hits = new List<SearchHit>(records.Count);
        foreach (var record in records)
        {
            // Скалярное произведение нормализованных векторов == косинусная близость.
            var score = DotProduct(queryVector, record.Vector);
            hits.Add(new SearchHit(record.ChunkId, score));
        }

        var result = hits
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToArray();

        _logger.LogDebug(
            "Векторный поиск: перебрано {Total} векторов, возвращено {Top}",
            hits.Count, result.Length);

        return result;
    }

    private static double DotProduct(float[] left, float[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        double sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += (double)left[i] * right[i];
        }

        return sum;
    }
}