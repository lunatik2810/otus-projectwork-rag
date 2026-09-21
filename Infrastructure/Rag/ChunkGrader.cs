using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Infrastructure.Ollama;

namespace OtusProjectworkRag.Infrastructure.Rag;

/// <summary>
/// Грейдинг чанков: локальная LLM (Ollama) оценивает, релевантен ли чанк
/// вопросу (да/нет). Это «корректирующий» шаг паттерна Corrective RAG.
/// </summary>
public interface IChunkGrader
{
    /// <summary>Оценивает каждый чанк из списка и возвращает результат с флагом релевантности.</summary>
    Task<IReadOnlyList<RelevantChunkResult>> GradeAsync(
        string question, IReadOnlyList<SearchResult> results, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IChunkGrader" />
public sealed class ChunkGrader : IChunkGrader
{
    private const string SystemPrompt =
        """
        Ты — фильтр релевантности для системы поиска по базе знаний (RAG).
        Определи, содержит ли фрагмент документа информацию, которая отвечает на вопрос.
        Отвечай СТРОГО одним JSON-объектом без пояснений и кода:
        {"relevant": true} или {"relevant": false}
        """;

    private static readonly Regex RelevantJsonRegex = new(
        "\"relevant\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IOllamaClient _ollama;
    private readonly ILogger<ChunkGrader> _logger;

    public ChunkGrader(IOllamaClient ollama, ILogger<ChunkGrader> logger)
    {
        _ollama = ollama;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RelevantChunkResult>> GradeAsync(
        string question, IReadOnlyList<SearchResult> results, CancellationToken cancellationToken)
    {
        var graded = new List<RelevantChunkResult>(results.Count);

        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relevant = await GradeOneAsync(question, result.Chunk.Text, cancellationToken);
            graded.Add(new RelevantChunkResult(
                Chunk: result.Chunk,
                VectorScore: result.VectorScore,
                Bm25Score: result.Bm25Score,
                RrfScore: result.RrfScore,
                Relevant: relevant));
        }

        var relevantCount = graded.Count(g => g.Relevant);
        _logger.LogInformation(
            "Грейдинг завершён: релевантных {Relevant} из {Total}", relevantCount, graded.Count);

        return graded;
    }

    private async Task<bool> GradeOneAsync(
        string question, string chunkText, CancellationToken cancellationToken)
    {
        try
        {
            var userPrompt =
                $"Вопрос:\n{question}\n\nФрагмент документа:\n{Truncate(chunkText)}";
            var answer = await _ollama.CompleteAsync(SystemPrompt, userPrompt, cancellationToken);
            return ParseRelevant(answer);
        }
        catch (Exception ex)
        {
            // Сбой LLM не должен ронять пайплайн: чанк считается нерелевантным,
            // при нехватке релевантных запрос будет расширен.
            _logger.LogWarning(ex, "Ошибка грейдинга чанка — считаем нерелевантным");
            return false;
        }
    }

    internal static bool ParseRelevant(string answer)
    {
        // 1) Пробуем строгий JSON.
        try
        {
            using var document = JsonDocument.Parse(answer);
            if (document.RootElement.TryGetProperty("relevant", out var relevantElement)
                && relevantElement.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (relevantElement.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }
        catch (JsonException)
        {
            // 2) Нестрогий разбор: ищем {"relevant": true/false} или голое слово.
        }

        var match = RelevantJsonRegex.Match(answer);
        if (match.Success)
        {
            return string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        // 3) Последний шанс: голые yes/no.
        if (answer.Contains("yes", StringComparison.OrdinalIgnoreCase)
            || answer.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value[..2000] + "…";
}