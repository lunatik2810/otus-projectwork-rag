namespace OtusProjectworkRag.Domain;

/// <summary>
/// Кусок текста документа (таблица Chunks).
/// </summary>
public sealed class Chunk
{
    /// <summary>Первичный ключ (используется как rowid в FTS5-индексе).</summary>
    public int Id { get; set; }

    /// <summary>Ссылка на документ (Documents.Id).</summary>
    public int DocumentId { get; set; }

    /// <summary>Порядковый номер чанка внутри документа (начиная с 0).</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Текст чанка.</summary>
    public required string Text { get; set; }

    /// <summary>Число токенов в чанке (посчитано токенизатором модели).</summary>
    public int TokenCount { get; set; }
}