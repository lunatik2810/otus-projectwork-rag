using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Domain;
using OtusProjectworkRag.Infrastructure.Data;
using OtusProjectworkRag.Infrastructure.Data.Repositories;
using OtusProjectworkRag.Infrastructure.Embeddings;
using OtusProjectworkRag.Infrastructure.Text;

namespace OtusProjectworkRag.Infrastructure.Indexing;

/// <summary>
/// Индексация папки с документами: сканирование → чанкинг → эмбеддинги → запись в SQLite.
/// Неизменённые файлы распознаются по SHA-256 хешу и пропускаются;
/// изменённые — переиндексируются полностью (старые чанки и векторы удаляются,
/// полнотекстовый индекс очищается триггером). Транзакция — на каждый файл
/// («всё или ничего»).
/// </summary>
public interface IIndexerService
{
    /// <summary>Индексирует файлы из папки по glob-паттерну.</summary>
    Task<IndexFolderResult> IndexAsync(
        string folderPath, string? globPattern, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IIndexerService" />
public sealed class IndexerService : IIndexerService
{
    private readonly IFileScanner _fileScanner;
    private readonly ITextChunker _chunker;
    private readonly IE5SmallEmbedder _embedder;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IDocumentRepository _documentRepository;
    private readonly IChunkRepository _chunkRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly ILogger<IndexerService> _logger;

    public IndexerService(
        IFileScanner fileScanner,
        ITextChunker chunker,
        IE5SmallEmbedder embedder,
        ISqliteConnectionFactory connectionFactory,
        IDocumentRepository documentRepository,
        IChunkRepository chunkRepository,
        IEmbeddingRepository embeddingRepository,
        IOptions<EmbeddingOptions> embeddingOptions,
        ILogger<IndexerService> logger)
    {
        _fileScanner = fileScanner;
        _chunker = chunker;
        _embedder = embedder;
        _connectionFactory = connectionFactory;
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IndexFolderResult> IndexAsync(
        string folderPath, string? globPattern, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var files = _fileScanner.Scan(folderPath, globPattern);
        _logger.LogInformation(
            "Индексация папки {Folder}: найдено файлов {Count}",
            folderPath, files.Count);

        var added = 0;
        var updated = 0;
        var unchanged = 0;
        var failed = 0;
        var totalChunks = 0;

        using var connection = _connectionFactory.CreateConnection();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fullPath = Path.GetFullPath(file);
                var hash = _fileScanner.ComputeContentHash(fullPath);

                // Неизменённые файлы распознаются по хешу и пропускаются.
                var existing = _documentRepository.FindBySourcePath(connection, null, fullPath);
                if (existing is not null && string.Equals(existing.ContentHash, hash, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }

                // Тяжёлые операции (чтение, чанкинг, эмбеддинги) — вне транзакции,
                // чтобы держать её короткой.
                var text = TextFileReader.ReadAllText(fullPath);
                var chunks = _chunker.Chunk(text);
                var vectors = await _embedder.EmbedAsync(
                    chunks.Select(c => c.Text).ToArray(), cancellationToken);

                using var transaction = connection.BeginTransaction();
                try
                {
                    var documentId = UpsertDocument(connection, transaction, fullPath, hash, existing, ref added, ref updated);

                    for (var i = 0; i < chunks.Count; i++)
                    {
                        var chunk = new Chunk
                        {
                            DocumentId = documentId,
                            ChunkIndex = i,
                            Text = chunks[i].Text,
                            TokenCount = chunks[i].TokenCount,
                        };

                        var chunkId = _chunkRepository.Insert(connection, transaction, chunk);

                        var embedding = new Embedding
                        {
                            ChunkId = chunkId,
                            Vector = vectors[i],
                            Dimensions = _embeddingOptions.Dimensions,
                        };
                        _embeddingRepository.Insert(connection, transaction, embedding);
                    }

                    transaction.Commit();
                    totalChunks += chunks.Count;

                    _logger.LogInformation(
                        "Файл {File}: чанков {Chunks}, токенов в среднем {AvgTokens}",
                        fullPath, chunks.Count,
                        chunks.Count > 0 ? chunks.Average(c => c.TokenCount) : 0);
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Ошибка индексации файла: {File}", file);
            }
        }

        stopwatch.Stop();

        var result = new IndexFolderResult(
            ScannedFiles: files.Count,
            Added: added,
            Updated: updated,
            Unchanged: unchanged,
            Failed: failed,
            Chunks: totalChunks,
            DurationMs: stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Индексация завершена: {Added} добавлено, {Updated} обновлено, " +
            "{Unchanged} без изменений, {Failed} ошибок, чанков {Chunks}, {DurationMs:F0} мс",
            result.Added, result.Updated, result.Unchanged, result.Failed,
            result.Chunks, result.DurationMs);

        return result;
    }

    /// <summary>
    /// Создаёт запись документа или обновляет существующую (хеш и дату),
    /// предварительно удалив старые чанки и векторы.
    /// </summary>
    private int UpsertDocument(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fullPath,
        string hash,
        Domain.Document? existing,
        ref int added,
        ref int updated)
    {
        if (existing is null)
        {
            var now = DateTime.UtcNow;
            var document = new Domain.Document
            {
                SourcePath = fullPath,
                FileName = Path.GetFileName(fullPath),
                ContentHash = hash,
                CreatedAt = now,
                UpdatedAt = now,
            };

            var documentId = _documentRepository.Insert(connection, transaction, document);
            added++;
            return documentId;
        }

        // Файл изменился: старые чанки удаляются целиком (FTS-индекс чистит
        // триггер chunks_ad), затем всё создаётся заново. Метаданные файла
        // при этом обновляются (где можно написать, что обновлён — обновляем).
        _embeddingRepository.DeleteByDocumentId(connection, transaction, existing.Id);
        _chunkRepository.DeleteByDocument(connection, transaction, existing.Id);

        existing.ContentHash = hash;
        existing.UpdatedAt = DateTime.UtcNow;
        _documentRepository.UpdateContent(connection, transaction, existing);

        updated++;
        return existing.Id;
    }
}