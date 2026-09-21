using Microsoft.Data.Sqlite;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Domain;

namespace OtusProjectworkRag.Infrastructure.Data.Repositories;

/// <summary>
/// Репозиторий таблицы Chunks. Удаление чанков автоматически очищает
/// полнотекстовый индекс ChunksFts через триггер chunks_ad.
/// </summary>
public interface IChunkRepository
{
    /// <summary>Вставляет чанк и возвращает его Id.</summary>
    int Insert(SqliteConnection connection, SqliteTransaction? transaction, Chunk chunk);

    /// <summary>Удаляет все чанки документа (FTS-индекс чистится триггером).</summary>
    void DeleteByDocument(SqliteConnection connection, SqliteTransaction? transaction, int documentId);

    /// <summary>
    /// Возвращает детали чанков по идентификаторам (с метаданными документа),
    /// сохраняя порядок переданного списка.
    /// </summary>
    IReadOnlyList<ChunkDetail> GetDetailsByIds(
        SqliteConnection connection, SqliteTransaction? transaction, IReadOnlyCollection<int> chunkIds);

    /// <summary>Число чанков в базе.</summary>
    int Count(SqliteConnection connection, SqliteTransaction? transaction);
}

/// <inheritdoc cref="IChunkRepository" />
public sealed class ChunkRepository : IChunkRepository
{
    public int Insert(SqliteConnection connection, SqliteTransaction? transaction, Chunk chunk)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Chunks (DocumentId, ChunkIndex, Text, TokenCount)
            VALUES ($documentId, $chunkIndex, $text, $tokenCount);
            """;
        command.Parameters.AddWithValue("$documentId", chunk.DocumentId);
        command.Parameters.AddWithValue("$chunkIndex", chunk.ChunkIndex);
        command.Parameters.AddWithValue("$text", chunk.Text);
        command.Parameters.AddWithValue("$tokenCount", chunk.TokenCount);
        command.ExecuteNonQuery();

        return GetLastInsertId(connection, transaction);
    }

    public void DeleteByDocument(SqliteConnection connection, SqliteTransaction? transaction, int documentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Chunks WHERE DocumentId = $documentId;";
        command.Parameters.AddWithValue("$documentId", documentId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ChunkDetail> GetDetailsByIds(
        SqliteConnection connection, SqliteTransaction? transaction, IReadOnlyCollection<int> chunkIds)
    {
        if (chunkIds.Count == 0)
        {
            return [];
        }

        // Строим плейсхолдеры вида @id0, @id1, ... для безопасного IN (...).
        var placeholders = string.Join(", ", chunkIds.Select((_, index) => $"$id{index}"));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT c.Id, c.ChunkIndex, c.Text, c.TokenCount,
                   c.DocumentId, d.FileName, d.SourcePath
            FROM Chunks c
            JOIN Documents d ON d.Id = c.DocumentId
            WHERE c.Id IN ({placeholders});
            """;

        var index = 0;
        foreach (var id in chunkIds)
        {
            command.Parameters.AddWithValue($"$id{index++}", id);
        }

        var byId = new Dictionary<int, ChunkDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var detail = new ChunkDetail(
                ChunkId: reader.GetInt32(0),
                ChunkIndex: reader.GetInt32(1),
                Text: reader.GetString(2),
                TokenCount: reader.GetInt32(3),
                DocumentId: reader.GetInt32(4),
                FileName: reader.GetString(5),
                SourcePath: reader.GetString(6));
            byId[detail.ChunkId] = detail;
        }

        // Сохраняем порядок запрошенных идентификаторов.
        return chunkIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
    }

    public int Count(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Chunks;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int GetLastInsertId(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}