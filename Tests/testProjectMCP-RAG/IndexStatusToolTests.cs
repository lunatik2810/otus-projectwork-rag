using Microsoft.Extensions.DependencyInjection;
using OtusProjectworkRag.Tools;
using Xunit;

namespace testProjectMCP_RAG;

/// <summary>
/// Тест MCP-инструмента index_status ([McpServerTool] <see cref="RagTools.IndexStatus"/>)
/// БЕЗ запуска MCP-сервера, по аналогии с <see cref="IndexFolderToolTests"/>.
/// Проверяется вся реальная цепочка: инструмент → MediatR → IndexStatusQueryHandler → SQLite.
///
/// Сценарий в одном тесте:
/// 1) Свежая (пустая) временная БД — статистика нулевая: документов, чанков и векторов
///    по 0, LastIndexedAt = null (в обработчике берётся max(UpdatedAt), документов нет).
/// 2) Индексируется ТОЛЬКО первый файл dossier_001.txt через glob-паттерн "dossier_001.txt"
///    (без разделителей пути — FileScanner сопоставляет паттерн с именем файла).
/// 3) Статистика запрашивается снова — показывает 1 документ, число чанков совпадает
///    со счётчиком из шага индексации, векторов столько же, сколько чанков,
///    LastIndexedAt заполнен, путь к БД не изменился.
/// </summary>
public class IndexStatusToolTests
{
    [Fact]
    public async Task IndexStatus_DirectCallWithoutMcpServer_EmptyThenSingleFile()
    {
        using var host = new RagTestHost();
        var tools = host.Services.GetRequiredService<RagTools>();

        // Шаг 1: БД пустая — все счётчики нулевые, последней индексации нет.
        var empty = await tools.IndexStatus(TestContext.Current.CancellationToken);

        Assert.Equal(0, empty.DocumentCount);
        Assert.Equal(0, empty.ChunkCount);
        Assert.Equal(0, empty.EmbeddingCount);
        Assert.Null(empty.LastIndexedAt);
        Assert.False(string.IsNullOrWhiteSpace(empty.DatabasePath));

        // Шаг 2: индексируем только первый файл dossier_001.txt.
        // Паттерн "dossier_001.txt" не содержит разделителей пути, поэтому FileScanner
        // сопоставляет его с именем файла — notes.md и остальные досье не попадают в выборку.
        var indexed = await tools.IndexFolder(
            host.DocumentsDir, "dossier_001.txt", TestContext.Current.CancellationToken);

        Assert.Equal(1, indexed.ScannedFiles);
        Assert.Equal(1, indexed.Added);
        Assert.Equal(0, indexed.Updated);
        Assert.Equal(0, indexed.Unchanged);
        Assert.Equal(0, indexed.Failed);
        Assert.True(indexed.Chunks > 0, "dossier_001.txt должен разбиться хотя бы на один чанк.");

        // Шаг 3: статистика показывает один документ; чанков и векторов поровну.
        var after = await tools.IndexStatus(TestContext.Current.CancellationToken);

        Assert.Equal(1, after.DocumentCount);
        Assert.Equal(indexed.Chunks, after.ChunkCount);
        Assert.Equal(after.ChunkCount, after.EmbeddingCount);
        Assert.NotNull(after.LastIndexedAt);
        Assert.Equal(empty.DatabasePath, after.DatabasePath);
    }
}