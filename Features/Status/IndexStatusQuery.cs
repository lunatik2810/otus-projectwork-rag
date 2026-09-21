using MediatR;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Infrastructure.Data;
using OtusProjectworkRag.Infrastructure.Data.Repositories;

namespace OtusProjectworkRag.Features.Status;

/// <summary>
/// Запрос статистики индекса (без обращения к LLM).
/// </summary>
public sealed record IndexStatusQuery : IRequest<IndexStatusResult>;

/// <summary>Обработчик статистики индекса.</summary>
public sealed class IndexStatusQueryHandler(
    ISqliteConnectionFactory connectionFactory,
    IDocumentRepository documentRepository,
    IChunkRepository chunkRepository,
    IEmbeddingRepository embeddingRepository) : IRequestHandler<IndexStatusQuery, IndexStatusResult>
{
    public Task<IndexStatusResult> Handle(IndexStatusQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        var documentCount = documentRepository.Count(connection, null);
        var chunkCount = chunkRepository.Count(connection, null);
        var embeddingCount = embeddingRepository.Count(connection, null);

        // Время последней индексации = максимальная дата обновления документа.
        DateTime? lastIndexedAt = null;
        var documents = documentRepository.GetAll(connection, null);
        if (documents.Count > 0)
        {
            lastIndexedAt = documents.Max(d => d.UpdatedAt);
        }

        return Task.FromResult(new IndexStatusResult(
            DocumentCount: documentCount,
            ChunkCount: chunkCount,
            EmbeddingCount: embeddingCount,
            LastIndexedAt: lastIndexedAt,
            DatabasePath: connectionFactory.DatabasePath));
    }
}