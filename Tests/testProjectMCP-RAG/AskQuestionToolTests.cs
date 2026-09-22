using Microsoft.Extensions.DependencyInjection;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Infrastructure.Ollama;
using OtusProjectworkRag.Tools;
using System.Net.Http;
using Xunit;

namespace testProjectMCP_RAG;

/// <summary>
/// Тесты MCP-инструмента ask_question ([McpServerTool] <see cref="RagTools.AskQuestion"/>)
/// БЕЗ запуска MCP-сервера, по аналогии с <see cref="FindRelevantDocsToolTests"/>.
/// Проверяется вся реальная цепочка: инструмент → MediatR → AskQuestionQueryHandler →
/// CorrectiveRagPipeline (гибридный поиск → грейдинг релевантности → расширение запроса) → SQLite.
///
/// Единственная внешняя зависимость — локальная LLM (Ollama). В большинстве тестов она
/// заменяется заглушкой <see cref="ScriptedOllamaClient"/> с детерминированными ответами
/// (без сети и без зависимости от машины); отдельный тест использует реальную Ollama.
/// Документы — копии досье из Resources/DataBaseRAG (5 .txt + notes.md вне выборки);
/// ключевые токены проверены по исходникам: «Миронов» — только dossier_001.txt,
/// «финансовые» — во всех пяти досье.
/// </summary>
public class AskQuestionToolTests
{
    private const string FirstDossier = "dossier_001.txt";

    /// <summary>
    /// Общий префикс: свежий хост с ЗАГЛУШКОЙ Ollama + индексация всех .txt-досье.
    /// Возвращает хост; вызывающий тест оформляет его через using.
    /// </summary>
    private static async Task<RagTestHost> CreateIndexedHostAsync(ScriptedOllamaClient ollama)
    {
        var host = new RagTestHost(ollama);
        var tools = host.Services.GetRequiredService<RagTools>();

        var indexed = await tools.IndexFolder(
            host.DocumentsDir, "*.txt", TestContext.Current.CancellationToken);

        Assert.Equal(host.DocCount, indexed.ScannedFiles);
        Assert.Equal(host.DocCount, indexed.Added);
        Assert.Equal(0, indexed.Failed);

        return host;
    }

    /// <summary>
    /// Общий префикс: свежий хост с РЕАЛЬНОЙ Ollama (без заглушки) + индексация.
    /// </summary>
    private static async Task<RagTestHost> CreateIndexedHostWithRealOllamaAsync()
    {
        var host = new RagTestHost();
        var tools = host.Services.GetRequiredService<RagTools>();

        var indexed = await tools.IndexFolder(
            host.DocumentsDir, "*.txt", TestContext.Current.CancellationToken);

        Assert.Equal(host.DocCount, indexed.ScannedFiles);
        Assert.Equal(host.DocCount, indexed.Added);
        Assert.Equal(0, indexed.Failed);

        return host;
    }

    /// <summary>
    /// Прямой вызов инструмента: заглушка помечает ВСЕ чанки релевантными —
    /// порог MinRelevant (3) достигнут на первой попытке, расширение запроса
    /// не требуется: Attempts=1, ExpandedQueries пуст.
    /// </summary>
    [Fact]
    public async Task AskQuestion_DirectCallWithoutMcpServer_AllChunksRelevant_ReturnsAfterFirstAttempt()
    {
        using var host = await CreateIndexedHostAsync(new ScriptedOllamaClient(gradeByBatch: [true]));
        var tools = host.Services.GetRequiredService<RagTools>();

        const string question = "Миронов";
        var result = await tools.AskQuestion(question, TestContext.Current.CancellationToken);

        Assert.Equal(question, result.Question);
        Assert.Equal(1, result.Attempts);
        Assert.Empty(result.ExpandedQueries);
        Assert.NotEmpty(result.Results);
        Assert.All(result.Results, r => Assert.True(r.Relevant, "Все чанки должны быть релевантными."));
        Assert.Contains(result.Results, r => r.Chunk.FileName == FirstDossier);
    }

    /// <summary>
    /// Устойчивость к недоступной Ollama: заглушка бросает исключение на каждый
    /// вызов (имитация отказа соединения). Пайплайн не падает: исчерпывает все
    /// попытки (1 исходный запрос + MaxExpansions=2 расширений), чанки помечаются
    /// нерелевантными, результат возвращается агенту.
    /// </summary>
    [Fact]
    public async Task AskQuestion_OllamaUnavailable_AllChunksIrrelevant_DoesNotCrash()
    {
        using var host = await CreateIndexedHostAsync(new ScriptedOllamaClient(throwOnCall: true));
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.AskQuestion("Миронов", TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Attempts);
        Assert.Equal(2, result.ExpandedQueries.Count);
        Assert.NotEmpty(result.Results);
        Assert.All(result.Results, r => Assert.False(r.Relevant, "При недоступной Ollama чанки нерелевантны."));
    }

    /// <summary>
    /// Corrective RAG-цикл: первая попытка грейдинга — все чанки НЕрелевантны
    /// (0 < MinRelevant=3), запрос расширяется и поиск повторяется; вторая попытка —
    /// все релевантны, цикл останавливается. Проверяется, что расширенный запрос
    /// действительно записан в ExpandedQueries и повторно использован в поиске.
    /// </summary>
    [Fact]
    public async Task AskQuestion_InsufficientRelevant_ExpandsQueryAndSearchesAgain()
    {
        using var host = await CreateIndexedHostAsync(
            new ScriptedOllamaClient(gradeByBatch: [false, true], expansion: "Миронов Миронова"));
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.AskQuestion("Миронов", TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Attempts);
        Assert.Single(result.ExpandedQueries);
        Assert.Equal("Миронов Миронова", result.ExpandedQueries[0]);
        Assert.NotEmpty(result.Results);
        Assert.All(result.Results, r => Assert.True(r.Relevant, "Вторая попытка должна вернуть релевантные чанки."));
    }

    /// <summary>
    /// Пустая база знаний: поиск ничего не находит, грейдить нечего, расширения
    /// не помогают — пайплайн отрабатывает все попытки и возвращает пустой
    /// результат без исключений.
    /// </summary>
    [Fact]
    public async Task AskQuestion_EmptyDatabase_ReturnsEmptyResult()
    {
        using var host = new RagTestHost(new ScriptedOllamaClient(expansion: "вопрос без ответа"));
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.AskQuestion("Миронов", TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Attempts);
        Assert.Equal(2, result.ExpandedQueries.Count);
        Assert.Empty(result.Results);
    }

    /// <summary>
    /// Метаданные и оценки заполнены: чанк связан с документом (путь внутри
    /// DocumentsDir), текст и число токенов непустые, RRF-оценка положительная.
    /// «финансовые» встречается во всех досье — поиск вернёт максимум чанков.
    /// </summary>
    [Fact]
    public async Task AskQuestion_ResultMetadataAndScores_AreFilled()
    {
        using var host = await CreateIndexedHostAsync(new ScriptedOllamaClient(gradeByBatch: [true]));
        var tools = host.Services.GetRequiredService<RagTools>();

        var result = await tools.AskQuestion("финансовые", TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Results);
        foreach (var item in result.Results)
        {
            Assert.True(item.Chunk.ChunkId > 0, "ChunkId должен быть положительным.");
            Assert.True(item.Chunk.TokenCount > 0, "TokenCount должен быть положительным.");
            Assert.False(string.IsNullOrWhiteSpace(item.Chunk.Text), "Текст чанка не может быть пустым.");
            Assert.False(string.IsNullOrWhiteSpace(item.Chunk.FileName), "FileName не может быть пустым.");
            Assert.StartsWith(host.DocumentsDir, item.Chunk.SourcePath);
            Assert.True(item.RrfScore > 0, "RRF-оценка найденного чанка должна быть положительной.");
            Assert.True(item.Relevant, "Заглушка помечает все чанки релевантными.");
        }
    }

    /// <summary>
    /// Тест с РЕАЛЬНОЙ Ollama: хост создаётся БЕЗ заглушки, работает настоящий
    /// OllamaClient (localhost:11434/v1, модель qwen2.5:3b из конфигурации).
    /// Проверки устойчивы к обоим сценариям:
    ///   - Ollama запущена и модель установлена → реальный грейдинг/расширение
    ///     (релевантные флаги зависят от LLM — строго не проверяем);
    ///   - Ollama недоступна → отказ соединения, чанки помечаются нерелевантными,
    ///     пайплайн не падает (устойчивость).
    /// Обязательны проверки, не зависящие от LLM: поиск вернул чанки,
    /// инвариант пайплайна ExpandedQueries.Count == Attempts - 1, метаданные заполнены.
    /// </summary>
    [Fact]
    public async Task AskQuestion_WithRealOllama_PipelineRunsAndReturnsChunks()
    {
        using var host = await CreateIndexedHostWithRealOllamaAsync();
        var tools = host.Services.GetRequiredService<RagTools>();

        string myQuestion = "Миронов";
        var result = await tools.AskQuestion(myQuestion, TestContext.Current.CancellationToken);

        Assert.Equal(myQuestion, result.Question);
        Assert.NotEmpty(result.Results);
        Assert.InRange(result.Attempts, 1, 3);
        Assert.Equal(result.Attempts - 1, result.ExpandedQueries.Count);
        Assert.All(result.Results, r =>
        {
            Assert.True(r.Chunk.ChunkId > 0, "ChunkId должен быть положительным.");
            Assert.False(string.IsNullOrWhiteSpace(r.Chunk.Text), "Текст чанка не может быть пустым.");
            Assert.False(string.IsNullOrWhiteSpace(r.Chunk.FileName), "FileName не может быть пустым.");
            Assert.StartsWith(host.DocumentsDir, r.Chunk.SourcePath);
        });
    }

    /// <summary>
    /// Тест с РЕАЛЬНОЙ Ollama: хост создаётся БЕЗ заглушки, работает настоящий
    /// OllamaClient (localhost:11434/v1, модель qwen2.5:3b из конфигурации).
    /// !!!Должен найти релевантные чанки
    /// Проверки устойчивы к обоим сценариям:
    ///   - Ollama запущена и модель установлена → реальный грейдинг/расширение
    ///     (релевантные флаги зависят от LLM — строго не проверяем);
    ///   - Ollama недоступна → отказ соединения, чанки помечаются нерелевантными,
    ///     пайплайн не падает (устойчивость).
    /// Обязательны проверки, не зависящие от LLM: поиск вернул чанки,
    /// инвариант пайплайна ExpandedQueries.Count == Attempts - 1, метаданные заполнены.
    /// </summary>
    [Fact]
    public async Task AskQuestion_WithRealOllama_PipelineRunsAndReturnsChunks_v2()
    {
        using var host = await CreateIndexedHostWithRealOllamaAsync();
        var tools = host.Services.GetRequiredService<RagTools>();

        string myQuestion = "Найди все ФИО";

        var result = await tools.AskQuestion(myQuestion, TestContext.Current.CancellationToken);

        // При работающей Ollama пайплайн должен вернуть хотя бы один релевантный чанк
        // (вопрос «Найди все ФИО» намеренно общий: в корпусе досье с ФИО много).
        Assert.Contains(result.Results, r => r.Relevant);
        Assert.Equal(myQuestion, result.Question);
        Assert.NotEmpty(result.Results);
        Assert.InRange(result.Attempts, 1, 3);
        Assert.Equal(result.Attempts - 1, result.ExpandedQueries.Count);
        Assert.All(result.Results, r =>
        {
            Assert.True(r.Chunk.ChunkId > 0, "ChunkId должен быть положительным.");
            Assert.False(string.IsNullOrWhiteSpace(r.Chunk.Text), "Текст чанка не может быть пустым.");
            Assert.False(string.IsNullOrWhiteSpace(r.Chunk.FileName), "FileName не может быть пустым.");
            Assert.StartsWith(host.DocumentsDir, r.Chunk.SourcePath);
        });
    }

    /// <summary>
    /// Заглушка локальной LLM (Ollama) для детерминированных тестов ask_question.
    /// Различает два типа вызовов по userPrompt (форматы заданы в ChunkGrader и
    /// QueryExpander):
    ///   «Вопрос:\n…»           — грейдинг чанка (ChunkGrader.GradeOneAsync);
    ///   «Исходный запрос: …»   — расширение запроса (QueryExpander.ExpandAsync).
    /// Вердикты грейдинга задаются ПОПЫТКАМИ (пакетами): подряд идущие вызовы
    /// грейдинга образуют один пакет, для каждого пакета берётся своё значение
    /// из <paramref name="gradeByBatch"/> (последнее повторяется, если пакетов больше).
    /// В режиме throwOnCall каждый вызов завершается исключением — имитация
    /// недоступной Ollama.
    /// </summary>
    private sealed class ScriptedOllamaClient : IOllamaClient
    {
        private readonly bool _throwOnCall;
        private readonly bool[] _gradeByBatch;
        private readonly string _expansion;
        private bool _previousWasGrade;
        private int _batchIndex = -1;

        public ScriptedOllamaClient(
            bool[]? gradeByBatch = null, string expansion = "", bool throwOnCall = false)
        {
            _gradeByBatch = gradeByBatch ?? [true];
            _expansion = expansion;
            _throwOnCall = throwOnCall;
        }

        public Task<string> CompleteAsync(
            string systemPrompt, string userPrompt, CancellationToken cancellationToken,
            double temperature = 0.0)
        {
            if (_throwOnCall)
            {
                // Отказ соединения — как при недоступном localhost:11434.
                return Task.FromException<string>(new HttpRequestException("Ollama недоступна"));
            }

            // Грейдинг: userPrompt начинается с «Вопрос:\n…».
            if (userPrompt.StartsWith("Вопрос:", StringComparison.Ordinal))
            {
                // Первый вызов нового пакета (попытки) — переходим к следующему вердикту.
                if (!_previousWasGrade)
                {
                    _batchIndex++;
                }
                _previousWasGrade = true;

                var relevant = _gradeByBatch[Math.Min(_batchIndex, _gradeByBatch.Length - 1)];
                return Task.FromResult(relevant ? """{"relevant": true}""" : """{"relevant": false}""");
            }

            // Расширение запроса: «Исходный запрос: …».
            _previousWasGrade = false;
            return Task.FromResult(_expansion);
        }
    }
}