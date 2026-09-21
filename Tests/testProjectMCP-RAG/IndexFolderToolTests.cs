using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OtusProjectworkRag.Features.Indexing;
using OtusProjectworkRag.Tools;
using Xunit;

namespace testProjectMCP_RAG;

/// <summary>
/// Первый тест: имитация вызова MCP-инструмента index_folder ([McpServerTool]
/// <see cref="RagTools.IndexFolder"/>) БЕЗ запуска MCP-сервера. Проверяется вся
/// реальная цепочка: инструмент → MediatR → IndexFolderRequestHandler → IndexerService → SQLite.
///
/// Два варианта, разница между ними — СЛОЙ вызова:
///
/// 1) Вариант A — ПРЯМОЙ вызов инструмента:
///    вызывается сам метод [McpServerTool] RagTools.IndexFolder (обёртка с логированием
///    и таймингом, внутри которой и происходит mediator.Send). Это ровно то, как
///    инструмент вызывается MCP-хостом, только без транспорта.
///
/// 2) Вариант B — ПОВТОР кода изнутри инструмента:
///    вызывается не метод IndexFolder, а его внутренняя строка
///    mediator.Send(new IndexFolderRequest(...)) — поэтому и название другое.
/// </summary>
public class IndexFolderToolTests
{
    /// <summary>
    /// Вариант A: прямой вызов [McpServerTool]-метода RagTools.IndexFolder без MCP-сервера.
    /// Первый прогон — все документы добавляются (Added=3); повторный — SHA-256 совпадает,
    /// файлы пропускаются (Unchanged=3).
    /// </summary>
    [Fact]
    public async Task IndexFolder_DirectCallWithoutMcpServer_IndexesFolderAndReturnsStats()
    {
        using var host = new RagTestHost();
        var tools = host.Services.GetRequiredService<RagTools>();

        // Прямое обращение к инструменту: tools.IndexFolder — это и есть
        // [McpServerTool] public async Task<IndexFolderResult> IndexFolder(...).
        // Первый прогон: все три .txt-файла новые — должны быть добавлены,
        // а notes.md при паттерне *.txt в выборку не попадает.
        var first = await tools.IndexFolder(
            host.DocumentsDir, "*.txt", TestContext.Current.CancellationToken);

        Assert.Equal(host.DocCount, first.ScannedFiles);
        Assert.Equal(host.DocCount, first.Added);
        Assert.Equal(0, first.Updated);
        Assert.Equal(0, first.Unchanged);
        Assert.Equal(0, first.Failed);
        Assert.True(first.Chunks > 0, "Индексация должна создать хотя бы один чанк.");
        Assert.True(first.DurationMs >= 0);

        // Повторный прогон тем же инструментом: хеши совпадают — файлы пропущены.
        var second = await tools.IndexFolder(
            host.DocumentsDir, "*.txt", TestContext.Current.CancellationToken);

        Assert.Equal(host.DocCount, second.ScannedFiles);
        Assert.Equal(0, second.Added);
        Assert.Equal(host.DocCount, second.Unchanged);
        Assert.Equal(0, second.Failed);
    }

    /// <summary>
    /// Вариант B: повтор кода, находящегося ВНУТРИ метода IndexFolder, — напрямую
    /// mediator.Send(new IndexFolderRequest(folderPath, globPattern)). Инструмент
    /// RagTools здесь НЕ вызывается, поэтому имя намеренно другое.
    /// </summary>
    [Fact]
    public async Task MediatorSend_IndexFolderRequest_IndexesFolderAndReturnsStats()
    {
        using var host = new RagTestHost();
        var mediator = host.Services.GetRequiredService<ISender>();

        // Та же строка, что и внутри RagTools.IndexFolder:
        // var result = await mediator.Send(new IndexFolderRequest(folderPath, globPattern), ct);
        var result = await mediator.Send(
            new IndexFolderRequest(host.DocumentsDir, "*.txt"),
            TestContext.Current.CancellationToken);

        Assert.Equal(host.DocCount, result.ScannedFiles);
        Assert.Equal(host.DocCount, result.Added);
        Assert.Equal(0, result.Failed);
        Assert.True(result.Chunks > 0, "Индексация должна создать хотя бы один чанк.");
    }
}