using Microsoft.Data.Sqlite;

namespace OtusProjectworkRag.Infrastructure.Data;

/// <summary>
/// Создаёт схему базы данных: таблицы Documents, Chunks, Embeddings,
/// виртуальную FTS5-таблицу ChunksFts и триггеры синхронизации полнотекстового индекса.
/// Идемпотентно — безопасно вызывать при каждом старте приложения.
/// </summary>
public static class SchemaInitializer
{
    /// <summary>Применяет схему к уже открытому соединению.</summary>
    public static void EnsureCreated(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Documents
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,

                SourcePath TEXT NOT NULL UNIQUE,

                FileName TEXT NOT NULL,

                ContentHash TEXT NOT NULL,

                CreatedAt TEXT NOT NULL,

                UpdatedAt TEXT NOT NULL
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Chunks
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,

                DocumentId INTEGER NOT NULL,

                ChunkIndex INTEGER NOT NULL,

                Text TEXT NOT NULL,

                TokenCount INTEGER NOT NULL,

                FOREIGN KEY (DocumentId)
                    REFERENCES Documents(Id)
            );
            """);

        // Уникальность пары «документ + номер» защищена индексом.
        Execute(connection, """
            CREATE UNIQUE INDEX IF NOT EXISTS UQ_Chunks_DocumentId_ChunkIndex
                ON Chunks(DocumentId, ChunkIndex);
            """);

        // Индекс по внешнему ключу для быстрого удаления чанков документа.
        Execute(connection, """
            CREATE INDEX IF NOT EXISTS IX_Chunks_DocumentId
                ON Chunks(DocumentId);
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Embeddings
            (
                ChunkId INTEGER PRIMARY KEY,

                Vector BLOB NOT NULL,

                Dimensions INTEGER NOT NULL,

                FOREIGN KEY (ChunkId)
                    REFERENCES Chunks(Id)
            );
            """);

        // Виртуальная полнотекстовая таблица для поиска по словам (BM25).
        // content='Chunks' — внешняя таблица-контент: текст живёт в Chunks,
        // а здесь хранится только индекс. Синхронизация — через триггеры ниже.
        Execute(connection, """
            CREATE VIRTUAL TABLE IF NOT EXISTS ChunksFts
            USING fts5
            (
                Text,

                content='Chunks',

                content_rowid='Id'
            );
            """);

        // Ключевой трюк — синхронизация текста и полнотекстового индекса через триггеры:
        //
        // 1) При вставке чанка триггер автоматически кладёт его текст в FTS-таблицу
        //    с тем же внутренним номером (rowid), что и у самого чанка — записи всегда совпадают.
        // 2) При удалении чанка (например, во время переиндексации документа) второй триггер
        //    автоматически удаляет соответствующую строку из FTS.
        //
        // Для внешней FTS5-таблицы удаление выполняется командой 'delete'
        // с текстом удаляемой записи.
        Execute(connection, """
            DROP TRIGGER IF EXISTS chunks_ai;
            CREATE TRIGGER chunks_ai AFTER INSERT ON Chunks
            BEGIN
                INSERT INTO ChunksFts(rowid, Text) VALUES (new.Id, new.Text);
            END;
            """);

        Execute(connection, """
            DROP TRIGGER IF EXISTS chunks_ad;
            CREATE TRIGGER chunks_ad AFTER DELETE ON Chunks
            BEGIN
                INSERT INTO ChunksFts(ChunksFts, rowid, Text) VALUES ('delete', old.Id, old.Text);
            END;
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}