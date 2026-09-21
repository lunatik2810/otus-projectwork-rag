namespace OtusProjectworkRag.Domain;

/// <summary>
/// Векторное представление чанка (таблица Embeddings).
/// Связь один-к-одному с чанком: ChunkId — первичный ключ.
/// </summary>
public sealed class Embedding
{
    /// <summary>Ссылка на чанк (Chunks.Id).</summary>
    public int ChunkId { get; set; }

    /// <summary>
    /// Нормализованный по L2 вектор. Для multilingual-e5-small размерность — 384.
    /// Хранится в базе как BLOB: float32 little-endian.
    /// </summary>
    public required float[] Vector { get; set; }

    /// <summary>Размерность вектора (384).</summary>
    public int Dimensions { get; set; }
}