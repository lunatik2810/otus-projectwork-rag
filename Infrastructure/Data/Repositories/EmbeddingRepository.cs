using Microsoft.Data.Sqlite;
using OtusProjectworkRag.Domain;

namespace OtusProjectworkRag.Infrastructure.Data.Repositories;

/// <summary>Запись эмбеддинга, прочитанная из базы (вектор расшифрован из BLOB).</summary>
public sealed record EmbeddingRecord(int ChunkId, float[] Vector);

/// <summary>
/// Репозиторий таблицы Embeddings (связь один-к-одному с чанком).
/// </summary>
public interface IEmbeddingRepository
{
    /// <summary>Вставляет вектор для чанка.</summary>
    void Insert(SqliteConnection connection, SqliteTransaction? transaction, Embedding embedding);

    /// <summary>Удаляет векторы всех чанков документа (необходимо до удаления чанков из-за внешнего ключа).</summary>
    void DeleteByDocumentId(SqliteConnection connection, SqliteTransaction? transaction, int documentId);

    /// <summary>Возвращает все векторы (используется для полного перебора при векторном поиске).</summary>
    IReadOnlyList<EmbeddingRecord> GetAll(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>Число векторов в базе.</summary>
    int Count(SqliteConnection connection, SqliteTransaction? transaction);
}

/// <inheritdoc cref="IEmbeddingRepository" />
public sealed class EmbeddingRepository : IEmbeddingRepository
{
    public void Insert(SqliteConnection connection, SqliteTransaction? transaction, Embedding embedding)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Embeddings (ChunkId, Vector, Dimensions)
            VALUES ($chunkId, $vector, $dimensions);
            """;
        command.Parameters.AddWithValue("$chunkId", embedding.ChunkId);
        command.Parameters.AddWithValue("$vector", VectorCodec.ToBlob(embedding.Vector));
        command.Parameters.AddWithValue("$dimensions", embedding.Dimensions);
        command.ExecuteNonQuery();
    }

    public void DeleteByDocumentId(
        SqliteConnection connection, SqliteTransaction? transaction, int documentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM Embeddings
            WHERE ChunkId IN (SELECT Id FROM Chunks WHERE DocumentId = $documentId);
            """;
        command.Parameters.AddWithValue("$documentId", documentId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<EmbeddingRecord> GetAll(
        SqliteConnection connection, SqliteTransaction? transaction)
    {
        var result = new List<EmbeddingRecord>();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ChunkId, Vector FROM Embeddings;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var chunkId = reader.GetInt32(0);
            var blob = (byte[])reader["Vector"];
            result.Add(new EmbeddingRecord(chunkId, VectorCodec.FromBlob(blob)));
        }

        return result;
    }

    public int Count(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Embeddings;";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}