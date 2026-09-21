using Microsoft.Extensions.Logging;
using OtusProjectworkRag.Infrastructure.Ollama;

namespace OtusProjectworkRag.Infrastructure.Rag;

/// <summary>
/// Расширение (переформулирование) запроса через локальную LLM.
/// Используется, когда релевантных чанков недостаточно: граф расширяет запрос
/// синонимами и близкими терминами и повторяет поиск.
/// </summary>
public interface IQueryExpander
{
    /// <summary>Возвращает более широкий вариант запроса.</summary>
    Task<string> ExpandAsync(string query, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IQueryExpander" />
public sealed class QueryExpander : IQueryExpander
{
    private const string SystemPrompt =
        """
        Ты помогаешь искать информацию в базе знаний.
        Перепиши запрос БОЛЕЕ ШИРОКО: добавь синонимы, близкие термины и
        альтернативные формулировки, чтобы охватить больше документов.
        Верни ТОЛЬКО переписанный запрос одной строкой, без кавычек, пояснений
        и нумерации. Язык — тот же, что у исходного запроса.
        """;

    private readonly IOllamaClient _ollama;
    private readonly ILogger<QueryExpander> _logger;

    public QueryExpander(IOllamaClient ollama, ILogger<QueryExpander> logger)
    {
        _ollama = ollama;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ExpandAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var expanded = await _ollama.CompleteAsync(
                SystemPrompt, $"Исходный запрос: {query}", cancellationToken, temperature: 0.2);

            // Очистка от лишних обёрток: кавычки, «Запрос:», точки в конце.
            expanded = expanded
                .Trim()
                .Trim('"', '\'', '«', '»', '-', ' ', '\n', '\r')
                .TrimEnd('.', '!', '?');

            if (string.IsNullOrWhiteSpace(expanded) || expanded.Length > 500)
            {
                throw new InvalidOperationException("Расширенный запрос пуст или слишком длинный.");
            }

            _logger.LogInformation("Запрос расширен: '{Original}' -> '{Expanded}'", query, expanded);
            return expanded;
        }
        catch (Exception ex)
        {
            // Если LLM недоступна — остаёмся на исходном запросе, поиск повторится без расширения.
            _logger.LogWarning(ex, "Не удалось расширить запрос '{Query}', используем исходный", query);
            return query;
        }
    }
}