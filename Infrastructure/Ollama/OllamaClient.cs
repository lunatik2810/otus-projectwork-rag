using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;

namespace OtusProjectworkRag.Infrastructure.Ollama;

/// <summary>
/// Клиент локальной LLM через Ollama (OpenAI-совместимый эндпоинт /v1/chat/completions).
/// Используется ТОЛЬКО для грейдинга релевантности чанков и расширения запроса —
/// финальный ответ формирует агент-хост.
/// </summary>
public interface IOllamaClient
{
    /// <summary>Выполняет чат-запрос и возвращает текст ответа модели.</summary>
    Task<string> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken,
        double temperature = 0.0);
}

/// <inheritdoc cref="IOllamaClient" />
public sealed class OllamaClient : IOllamaClient
{
    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaClient> _logger;

    public OllamaClient(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // Базовый адрес задаётся в конфигурации (в Docker — host.docker.internal).
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken,
        double temperature = 0.0)
    {
        var request = new ChatCompletionRequest
        {
            Model = _options.Model,
            Temperature = temperature,
            Stream = false,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userPrompt },
            ],
        };

        using var response = await _httpClient.PostAsJsonAsync("chat/completions", request, RequestOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(RequestOptions, cancellationToken)
            ?? throw new InvalidOperationException("Пустой ответ от Ollama.");

        var content = body.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Ollama не вернул содержимое ответа.");
        }

        _logger.LogDebug("Ollama ({Model}) ответила: {Preview}", _options.Model, Truncate(content));
        return content;
    }

    private static string Truncate(string value) =>
        value.Length <= 200 ? value : value[..200] + "…";

    private sealed record ChatCompletionRequest
    {
        public required string Model { get; init; }

        public required IReadOnlyList<ChatMessage> Messages { get; init; }

        public double Temperature { get; init; }

        public bool Stream { get; init; }
    }

    private sealed record ChatMessage
    {
        public required string Role { get; init; }

        public required string Content { get; init; }
    }

    private sealed record ChatCompletionResponse
    {
        public IReadOnlyList<Choice>? Choices { get; init; }
    }

    private sealed record Choice
    {
        public ChatMessage? Message { get; init; }
    }
}