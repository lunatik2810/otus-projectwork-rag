namespace OtusProjectworkRag.Contracts;

/// <summary>
/// Результат грейдинга: чанк с оценками поиска и вердиктом LLM о релевантности.
/// </summary>
public sealed record RelevantChunkResult(
    ChunkDetail Chunk,
    double VectorScore,
    double Bm25Score,
    double RrfScore,
    bool Relevant);

/// <summary>
/// Результат полного RAG-пайплайна (ask_question): отобранные и проверенные
/// чанки передаются агенту-хосту, который формулирует финальный ответ.
/// </summary>
public sealed record AskQuestionResult(
    string Question,
    int Attempts,
    IReadOnlyList<string> ExpandedQueries,
    IReadOnlyList<RelevantChunkResult> Results);