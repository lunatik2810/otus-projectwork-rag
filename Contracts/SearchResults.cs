namespace OtusProjectworkRag.Contracts;

/// <summary>
/// Результат гибридного поиска: чанк с метаданными документа и оценками
/// из векторного поиска, BM25 и итоговой RRF.
/// </summary>
public sealed record SearchResult(
    ChunkDetail Chunk,
    double VectorScore,
    double Bm25Score,
    double RrfScore);