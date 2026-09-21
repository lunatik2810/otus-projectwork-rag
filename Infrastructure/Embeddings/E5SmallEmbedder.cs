using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OtusProjectworkRag.Configuration;

namespace OtusProjectworkRag.Infrastructure.Embeddings;

/// <summary>
/// Построение эмбеддингов через ONNX-модель multilingual-e5-small (384 измерения).
///
/// Особенности модели:
///  - обязательные префиксы: "passage: " для документов, "query: " для запросов —
///    смешивать их нельзя (модель обучена на парах «запрос-документ»);
///  - нижний регистр перед эмбеддингом (рекомендация карточки модели);
///  - среднее по токенам (mean pooling) по маске внимания, затем L2-нормализация
///    — косинусная близость превращается в дешёвое скалярное произведение.
/// </summary>
public interface IE5SmallEmbedder
{
    /// <summary>Эмбеддинги текстов с префиксом "passage: " (для индексации).</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

    /// <summary>Эмбеддинг запроса с префиксом "query: ".</summary>
    Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IE5SmallEmbedder" />
public sealed class E5SmallEmbedder : IE5SmallEmbedder, IDisposable
{
    private readonly ITokenizerProvider _tokenizerProvider;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<E5SmallEmbedder> _logger;
    private readonly InferenceSession _session;

    // Имена входов/выходов определяются автоматически из метаданных модели,
    // чтобы не зависеть от того, как назвал их экспорт.
    private readonly string _inputIdsName;
    private readonly string _attentionMaskName;
    private readonly string _tokenTypeIdsName;
    private readonly string _outputName;

    // Защищает вызовы к native-токенизатору и инференсу от параллельного доступа.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public E5SmallEmbedder(
        IOptions<EmbeddingOptions> options,
        ITokenizerProvider tokenizerProvider,
        ILogger<E5SmallEmbedder> logger)
    {
        _options = options.Value;
        _tokenizerProvider = tokenizerProvider;
        _logger = logger;

        var modelDirectory = AppPaths.ResolveDirectory(_options.ModelDirectory);
        var modelPath = Path.Combine(modelDirectory, _options.ModelFileName);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Файл ONNX-модели не найден: {modelPath}", modelPath);
        }

        // SessionOptions квалифицируем полностью: имя конфликтует с Microsoft.AspNetCore.Builder.SessionOptions.
        _session = new InferenceSession(
            modelPath,
            new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            });

        // Автоопределение имён входов по содержимому названий.
        var inputKeys = _session.InputMetadata.Keys.ToArray();
        _inputIdsName = inputKeys.First(k => k.Contains("input_ids", StringComparison.OrdinalIgnoreCase));
        _attentionMaskName = inputKeys.First(k => k.Contains("attention", StringComparison.OrdinalIgnoreCase));
        _tokenTypeIdsName = inputKeys.FirstOrDefault(
            k => k.Contains("token_type", StringComparison.OrdinalIgnoreCase)) ?? "token_type_ids";
        _outputName = _session.OutputMetadata.Keys.First();

        _logger.LogInformation(
            "ONNX-модель загружена: {ModelPath}; входы: {Inputs}; выход: {Output}",
            modelPath, string.Join(", ", inputKeys), _outputName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var result = new List<float[]>(texts.Count);

        // Обрабатываем текст пакетами (BatchSize), чтобы не держать в памяти
        // большой батч и быстрее получить первые результаты.
        foreach (var batch in texts.Chunk(_options.BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embeddings = await Task.Run(() => EmbedBatch(batch, "passage: "), cancellationToken);
            result.AddRange(embeddings);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var embeddings = await Task.Run(() => EmbedBatch([query], "query: "), cancellationToken);
        return embeddings[0];
    }

    private IReadOnlyList<float[]> EmbedBatch(IReadOnlyList<string> texts, string prefix)
    {
        // Держим под замком, так как native-токенизатор и сессия ONNX
        // разделяются между потоками.
        _gate.Wait();
        try
        {
            // Нижний регистр + обязательный префикс (e5 обучена на нижнем регистре).
            var prepared = texts.Select(t => prefix + t.ToLowerInvariant()).ToArray();
            var encoded = prepared.Select(_tokenizerProvider.Encode).ToArray();

            // Длина каждого входа ограничена моделью (512 токенов),
            // а внутри батча — паддинг до максимальной длины в батче.
            var maxSequenceLength = _options.MaxSequenceLength;
            var lengths = encoded
                .Select(e => Math.Min(e.Ids.Length, maxSequenceLength))
                .ToArray();
            var batchLength = lengths.Max();
            if (batchLength == 0)
            {
                throw new InvalidOperationException("Пустая последовательность для эмбеддинга.");
            }

            var batchSize = texts.Count;
            var inputIds = new long[batchSize * batchLength];
            var attentionMask = new long[batchSize * batchLength];
            var tokenTypeIds = new long[batchSize * batchLength]; // по умолчанию нули

            for (var i = 0; i < batchSize; i++)
            {
                var ids = encoded[i].Ids;
                var count = lengths[i];
                for (var j = 0; j < count; j++)
                {
                    inputIds[i * batchLength + j] = ids[j];
                    attentionMask[i * batchLength + j] = 1;
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(
                    _inputIdsName,
                    new DenseTensor<long>(inputIds, new[] { batchSize, batchLength })),
                NamedOnnxValue.CreateFromTensor(
                    _attentionMaskName,
                    new DenseTensor<long>(attentionMask, new[] { batchSize, batchLength })),
                NamedOnnxValue.CreateFromTensor(
                    _tokenTypeIdsName,
                    new DenseTensor<long>(tokenTypeIds, new[] { batchSize, batchLength })),
            };

            using var results = _session.Run(inputs);
            var output = results[0].AsTensor<float>();
            var hiddenSize = output.Dimensions[2];
            var flat = output.ToArray();

            var embeddings = new List<float[]>(batchSize);
            for (var i = 0; i < batchSize; i++)
            {
                var embedding = MeanPool(flat, i, batchLength, hiddenSize, lengths[i]);
                L2Normalize(embedding);
                embeddings.Add(embedding);
            }

            return embeddings;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Усредняет скрытые состояния по всем реальным токенам (не паддингу).
    /// Входной тензор — плоский массив [batch, seq, hidden].
    /// </summary>
    private static float[] MeanPool(float[] flat, int batchIndex, int seqLength, int hiddenSize, int tokenCount)
    {
        var embedding = new float[hiddenSize];

        for (var d = 0; d < hiddenSize; d++)
        {
            double sum = 0;
            for (var s = 0; s < tokenCount; s++)
            {
                sum += flat[(batchIndex * seqLength + s) * hiddenSize + d];
            }

            embedding[d] = tokenCount > 0 ? (float)(sum / tokenCount) : 0f;
        }

        return embedding;
    }

    /// <summary>L2-нормализация: после неё косинус = скалярное произведение.</summary>
    private static void L2Normalize(float[] vector)
    {
        double normSquared = 0;
        foreach (var value in vector)
        {
            normSquared += (double)value * value;
        }

        var norm = Math.Sqrt(normSquared);
        if (norm <= 1e-12)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _session.Dispose();
        _gate.Dispose();
    }
}