namespace OtusProjectworkRag.Contracts;

/// <summary>
/// Результат инструмента find_relevant_docs: ранжированные фрагменты
/// с метаданными (файл, идентификатор чанка, оценки близости).
/// </summary>
public sealed record FindRelevantDocsResult(
    string Query,
    int TopK,
    IReadOnlyList<SearchResult> Results);