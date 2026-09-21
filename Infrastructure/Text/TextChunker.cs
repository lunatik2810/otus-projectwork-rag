using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;

namespace OtusProjectworkRag.Infrastructure.Text;

/// <summary>Результат чанкинга: текст фрагмента и число токенов.</summary>
public sealed record TextChunk(string Text, int TokenCount);

/// <summary>
/// Разбивает текст на чанки.
/// Сначала текст делится на «куски» по границам предложений и переводам строк,
/// затем куски склеиваются в чанки с ограничением MaxTokens и перекрытием OverlapTokens.
/// Число токенов каждого куска считается заранее, одним проходом токенизатора,
/// чтобы не считать дважды.
/// </summary>
public interface ITextChunker
{
    /// <summary>Разбивает исходный текст на чанки.</summary>
    IReadOnlyList<TextChunk> Chunk(string text);
}

/// <inheritdoc cref="ITextChunker" />
public sealed class TextChunker : ITextChunker
{
    // Разбиваем либо после знака конца предложения перед заглавной буквой,
    // либо по переводу строки. Lookbehind (?<=\r?\n) сохраняет перевод строки
    // в предыдущей части.
    private static readonly Regex SplitPattern = new(
        @"(?<=[.!?…]\s*)(?=[А-ЯЁA-Z])|(?<=\r?\n)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ITokenCounter _tokenCounter;
    private readonly ChunkingOptions _options;

    public TextChunker(ITokenCounter tokenCounter, IOptions<ChunkingOptions> options)
    {
        _tokenCounter = tokenCounter;
        _options = options.Value;
    }

    public IReadOnlyList<TextChunk> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var parts = SplitIntoParts(text);

        // Один проход токенизатора: считаем токены каждого куска заранее.
        var partTokens = parts.Select(_tokenCounter.CountTokens).ToArray();

        var chunks = new List<TextChunk>();
        var buffer = new List<(string Text, int Tokens)>();

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var tokens = partTokens[i];

            // Если следующий кусок не помещается — сбрасываем текущий чанк
            // и формируем перекрытие из хвоста буфера.
            if (buffer.Count > 0 && bufferTokens(buffer) + tokens > _options.MaxTokens)
            {
                Flush(buffer, chunks);

                // Перекрытие: переносим в новый чанк последние куски буфера,
                // суммарно не превышающие OverlapTokens, чтобы смысл,
                // разорванный границей чанка, не терялся.
                var (carry, carryTokens) = TakeOverlap(buffer, _options.OverlapTokens);
                buffer = carry;
            }

            buffer.Add((part, tokens));
        }

        if (buffer.Count > 0)
        {
            Flush(buffer, chunks);
        }

        return chunks;
    }

    private static IReadOnlyList<string> SplitIntoParts(string text)
    {
        // Ищем все границы (позиции разрезов). Distinct защищает от дублей,
        // когда обе альтернативы совпадают в одной позиции.
        var boundaries = SplitPattern
            .Matches(text)
            .Select(m => m.Index)
            .Distinct()
            .ToArray();

        if (boundaries.Length == 0)
        {
            return [text];
        }

        var parts = new List<string>(boundaries.Length + 1);
        var start = 0;
        foreach (var boundary in boundaries)
        {
            parts.Add(text[start..boundary]);
            start = boundary;
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static int bufferTokens(List<(string Text, int Tokens)> buffer) =>
        buffer.Sum(b => b.Tokens);

    private static void Flush(List<(string Text, int Tokens)> buffer, List<TextChunk> chunks)
    {
        var text = string.Concat(buffer.Select(b => b.Text));
        var tokens = buffer.Sum(b => b.Tokens);
        chunks.Add(new TextChunk(text, tokens));
    }

    private static (List<(string Text, int Tokens)> Carry, int Tokens) TakeOverlap(
        List<(string Text, int Tokens)> buffer, int overlapTokens)
    {
        var carry = new List<(string Text, int Tokens)>();
        var tokens = 0;

        for (var i = buffer.Count - 1; i >= 0; i--)
        {
            if (tokens + buffer[i].Tokens > overlapTokens)
            {
                break;
            }

            carry.Insert(0, buffer[i]);
            tokens += buffer[i].Tokens;
        }

        return (carry, tokens);
    }
}