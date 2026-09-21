using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;
using OtusProjectworkRag.Infrastructure.Text;
using Tokenizers.HuggingFace.Tokenizer;

namespace OtusProjectworkRag.Infrastructure.Embeddings;

/// <summary>Результат кодирования: идентификаторы токенов и маска внимания.</summary>
public sealed record TokenizedSequence(int[] Ids, int[] AttentionMask);

/// <summary>
/// Провайдер токенизатора multilingual-e5-small (файл tokenizer.json).
/// Загружает токенизатор один раз (singleton) и предоставляет:
///  - подсчёт токенов для чанкинга (ITokenCounter);
///  - кодирование текста в идентификаторы токенов и маску внимания для ONNX-инференса.
/// </summary>
public interface ITokenizerProvider : ITokenCounter
{
    /// <summary>Кодирует текст со специальными токенами (<s> ... </s>).</summary>
    TokenizedSequence Encode(string text);
}

/// <inheritdoc cref="ITokenizerProvider" />
public sealed class TokenizerProvider : ITokenizerProvider, IDisposable
{
    private readonly Tokenizer _tokenizer;

    public TokenizerProvider(
        IOptions<EmbeddingOptions> options,
        ILogger<TokenizerProvider> logger)
    {
        var modelDirectory = AppPaths.ResolveDirectory(options.Value.ModelDirectory);
        var tokenizerPath = Path.Combine(modelDirectory, options.Value.TokenizerFileName);

        if (!File.Exists(tokenizerPath))
        {
            throw new FileNotFoundException(
                $"Файл токенизатора не найден: {tokenizerPath}", tokenizerPath);
        }

        _tokenizer = Tokenizer.FromFile(tokenizerPath);

        logger.LogInformation("Токенизатор загружен: {Path}", tokenizerPath);
    }

    /// <inheritdoc />
    public int CountTokens(string text)
    {
        // Без специальных токенов — считаем только реальные токены текста.
        var encoding = _tokenizer.Encode(text, addSpecialTokens: false).First();
        return encoding.Ids.Count;
    }

    /// <inheritdoc />
    public TokenizedSequence Encode(string text)
    {
        var encoding = _tokenizer
            .Encode(text, addSpecialTokens: true, includeTypeIds: true, includeAttentionMask: true)
            .First();

        // Ids и AttentionMask приходят как uint[] — приводим к int[].
        return new TokenizedSequence(
            Ids: encoding.Ids.Select(id => (int)id).ToArray(),
            AttentionMask: encoding.AttentionMask.Select(m => (int)m).ToArray());
    }

    /// <inheritdoc />
    public void Dispose() => _tokenizer.Dispose();
}