namespace OtusProjectworkRag.Contracts;

/// <summary>
/// Статистика индекса (index_status): число документов, чанков, векторов,
/// время последней индексации и путь к базе.
/// </summary>
public sealed record IndexStatusResult(
    int DocumentCount,
    int ChunkCount,
    int EmbeddingCount,
    DateTime? LastIndexedAt,
    string DatabasePath);