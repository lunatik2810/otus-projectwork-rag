namespace OtusProjectworkRag.Domain;

/// <summary>
/// Метаданные об исходном файле документа (таблица Documents).
/// </summary>
public sealed class Document
{
    /// <summary>Первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Полный путь к исходному файлу (уникальный).</summary>
    public required string SourcePath { get; set; }

    /// <summary>Имя файла без пути.</summary>
    public required string FileName { get; set; }

    /// <summary>
    /// SHA-256 хеш содержимого файла.
    /// По нему определяется, изменился ли файл с момента последней индексации.
    /// </summary>
    public required string ContentHash { get; set; }

    /// <summary>Дата создания записи в базе (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Дата последнего обновления (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}