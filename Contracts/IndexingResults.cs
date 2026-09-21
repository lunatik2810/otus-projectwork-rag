namespace OtusProjectworkRag.Contracts;

/// <summary>
/// Результат индексации папки — статистика по обработанным файлам.
/// </summary>
public sealed record IndexFolderResult(
    int ScannedFiles,
    int Added,
    int Updated,
    int Unchanged,
    int Failed,
    int Chunks,
    double DurationMs);