using Microsoft.Data.Sqlite;
using OtusProjectworkRag.Domain;

namespace OtusProjectworkRag.Infrastructure.Data.Repositories;

/// <summary>
/// Репозиторий таблицы Documents.
/// Все методы принимают уже открытое соединение и, опционально, транзакцию —
/// владеет транзакцией вызывающий код (сервис индексации).
/// </summary>
public interface IDocumentRepository
{
    /// <summary>Находит документ по уникальному пути исходного файла.</summary>
    Document? FindBySourcePath(SqliteConnection connection, SqliteTransaction? transaction, string sourcePath);

    /// <summary>Вставляет документ и возвращает его Id.</summary>
    int Insert(SqliteConnection connection, SqliteTransaction? transaction, Document document);

    /// <summary>Обновляет хеш и дату обновления (контент файла изменился).</summary>
    void UpdateContent(SqliteConnection connection, SqliteTransaction? transaction, Document document);

    /// <summary>Удаляет документ вместе с метаданными.</summary>
    void Delete(SqliteConnection connection, SqliteTransaction? transaction, int documentId);

    /// <summary>Возвращает все документы (для статистики).</summary>
    IReadOnlyList<Document> GetAll(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>Число документов в базе.</summary>
    int Count(SqliteConnection connection, SqliteTransaction? transaction);
}

/// <inheritdoc cref="IDocumentRepository" />
public sealed class DocumentRepository : IDocumentRepository
{
    private const string SelectColumns =
        "Id, SourcePath, FileName, ContentHash, CreatedAt, UpdatedAt";

    public Document? FindBySourcePath(
        SqliteConnection connection, SqliteTransaction? transaction, string sourcePath)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {SelectColumns} FROM Documents WHERE SourcePath = $path;";
        command.Parameters.AddWithValue("$path", sourcePath);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public int Insert(SqliteConnection connection, SqliteTransaction? transaction, Document document)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Documents (SourcePath, FileName, ContentHash, CreatedAt, UpdatedAt)
            VALUES ($sourcePath, $fileName, $contentHash, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$sourcePath", document.SourcePath);
        command.Parameters.AddWithValue("$fileName", document.FileName);
        command.Parameters.AddWithValue("$contentHash", document.ContentHash);
        command.Parameters.AddWithValue("$createdAt", SqliteDate.Format(document.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqliteDate.Format(document.UpdatedAt));
        command.ExecuteNonQuery();

        return GetLastInsertId(connection, transaction);
    }

    public void UpdateContent(SqliteConnection connection, SqliteTransaction? transaction, Document document)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Documents
            SET ContentHash = $contentHash, UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$contentHash", document.ContentHash);
        command.Parameters.AddWithValue("$updatedAt", SqliteDate.Format(document.UpdatedAt));
        command.Parameters.AddWithValue("$id", document.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(SqliteConnection connection, SqliteTransaction? transaction, int documentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Documents WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", documentId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Document> GetAll(SqliteConnection connection, SqliteTransaction? transaction)
    {
        var result = new List<Document>();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {SelectColumns} FROM Documents ORDER BY SourcePath;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public int Count(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Documents;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static Document Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        SourcePath = reader.GetString(1),
        FileName = reader.GetString(2),
        ContentHash = reader.GetString(3),
        CreatedAt = SqliteDate.Parse(reader.GetString(4)),
        UpdatedAt = SqliteDate.Parse(reader.GetString(5)),
    };

    private static int GetLastInsertId(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}