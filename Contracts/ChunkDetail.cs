namespace OtusProjectworkRag.Contracts;

/// <summary>
/// Чанк вместе с метаданными документа — единица результата поиска.
/// Содержит то, что требуется отдавать агенту: файл, идентификатор чанка, текст.
/// </summary>
public sealed record ChunkDetail(
    int ChunkId,
    int ChunkIndex,
    string Text,
    int TokenCount,
    int DocumentId,
    string FileName,
    string SourcePath);