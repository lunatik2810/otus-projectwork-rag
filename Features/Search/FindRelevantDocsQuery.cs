using MediatR;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Infrastructure.Search;

namespace OtusProjectworkRag.Features.Search;

/// <summary>
/// Поиск релевантных чанков без генерации ответа (гибридный поиск: BM25 + вектор → RRF).
/// LLM не используется.
/// </summary>
public sealed record FindRelevantDocsQuery(
    string Query,
    int TopK = 10) : IRequest<IReadOnlyList<SearchResult>>;

/// <summary>Обработчик поиска релевантных документов.</summary>
public sealed class FindRelevantDocsQueryHandler(
    IHybridSearchService search) : IRequestHandler<FindRelevantDocsQuery, IReadOnlyList<SearchResult>>
{
    public Task<IReadOnlyList<SearchResult>> Handle(
        FindRelevantDocsQuery request, CancellationToken cancellationToken)
        => search.SearchAsync(request.Query, request.TopK, cancellationToken);
}