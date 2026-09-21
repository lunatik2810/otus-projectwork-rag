using MediatR;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Infrastructure.Indexing;

namespace OtusProjectworkRag.Features.Indexing;

/// <summary>
/// Запрос на индексацию папки с документами.
/// Request и Handler размещены рядом в одном файле для удобства навигации.
/// </summary>
public sealed record IndexFolderRequest(
    string FolderPath,
    string? GlobPattern = null) : IRequest<IndexFolderResult>;

/// <summary>Обработчик индексации папки.</summary>
public sealed class IndexFolderRequestHandler(
    IIndexerService indexer) : IRequestHandler<IndexFolderRequest, IndexFolderResult>
{
    public Task<IndexFolderResult> Handle(IndexFolderRequest request, CancellationToken cancellationToken)
        => indexer.IndexAsync(request.FolderPath, request.GlobPattern, cancellationToken);
}