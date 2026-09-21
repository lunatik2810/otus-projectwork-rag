namespace OtusProjectworkRag.Configuration;

/// <summary>Настройки расположения базы данных (секция "Database").</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Путь к файлу SQLite-базы vectorDb (относительный или абсолютный).</summary>
    public string Path { get; init; } = "Resources/vectorDb.db";
}

/// <summary>Настройки модели эмбеддингов multilingual-e5-small (секция "Embedding").</summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    /// <summary>Каталог с моделью и токенизатором (относительно каталога приложения).</summary>
    public string ModelDirectory { get; init; } = "Resources/multilingual-e5-small";

    /// <summary>Имя ONNX-файла модели.</summary>
    public string ModelFileName { get; init; } = "model_O4.onnx";

    /// <summary>Имя файла токенизатора в формате HuggingFace tokenizer.json.</summary>
    public string TokenizerFileName { get; init; } = "tokenizer.json";

    /// <summary>Размер батча при инференсе.</summary>
    public int BatchSize { get; init; } = 16;

    /// <summary>Максимальная длина последовательности в токенах (ограничение модели — 512).</summary>
    public int MaxSequenceLength { get; init; } = 512;

    /// <summary>Размерность вектора эмбеддинга (384).</summary>
    public int Dimensions { get; init; } = 384;
}

/// <summary>Настройки чанкинга (секция "Chunking").</summary>
public sealed class ChunkingOptions
{
    public const string SectionName = "Chunking";

    /// <summary>Максимальное число токенов в одном чанке.</summary>
    public int MaxTokens { get; init; } = 470;

    /// <summary>Перекрытие между соседними чанками (в токенах).</summary>
    public int OverlapTokens { get; init; } = 20;
}

/// <summary>Настройки индексации (секция "Indexing").</summary>
public sealed class IndexingOptions
{
    public const string SectionName = "Indexing";

    /// <summary>Глоб-паттерн для инструмента index_folder по умолчанию.</summary>
    public string DefaultGlob { get; init; } = "*.txt";
}

/// <summary>Настройки гибридного поиска и Corrective RAG (секция "Retrieval").</summary>
public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    /// <summary>Сколько чанков берём из BM25-списка.</summary>
    public int Bm25TopK { get; init; } = 50;

    /// <summary>Сколько чанков берём из векторного списка.</summary>
    public int VectorTopK { get; init; } = 50;

    /// <summary>Параметр k алгоритма Reciprocal Rank Fusion (константа сглаживания).</summary>
    public int RrfK { get; init; } = 60;

    /// <summary>Сколько чанков возвращаем после слияния RRF.</summary>
    public int FinalTopK { get; init; } = 10;

    /// <summary>Минимальное число релевантных чанков, при котором цикл останавливается.</summary>
    public int MinRelevant { get; init; } = 3;

    /// <summary>Максимальное число расширений запроса (повторных поисков).</summary>
    public int MaxExpansions { get; init; } = 2;
}

/// <summary>Настройки подключения к локальной LLM через Ollama (секция "Ollama").</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Базовый URL OpenAI-совместимого API (по умолчанию http://localhost:11434/v1).</summary>
    public string BaseUrl { get; init; } = "http://localhost:11434/v1";

    /// <summary>Имя модели (например, qwen2.5:3b).</summary>
    public string Model { get; init; } = "qwen2.5:3b";

    /// <summary>Таймаут HTTP-запроса к Ollama в секундах.</summary>
    public int TimeoutSeconds { get; init; } = 120;
}