using Microsoft.Data.Sqlite;

namespace OtusProjectworkRag.Infrastructure.Data;

/// <summary>
/// Фабрика соединений с SQLite.
/// Каждое соединение открывается с включёнными внешними ключами и WAL-режимом
/// (журнал, позволяющий читать базу во время записи).
/// </summary>
public interface ISqliteConnectionFactory
{
    /// <summary>Полный путь к файлу базы данных.</summary>
    string DatabasePath { get; }

    /// <summary>Создаёт и открывает новое соединение с базой.</summary>
    SqliteConnection CreateConnection();
}

/// <summary>
/// Реализация фабрики. Зарегистрирована как singleton в DI.
/// Владелец соединения (сервис индексации/поиска) отвечает за его закрытие.
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;

    /// <inheritdoc />
    public string DatabasePath { get; }

    public SqliteConnectionFactory(string databasePath)
    {
        // Приводим к абсолютному пути и заранее создаём каталог,
        // чтобы SQLite мог создать файл базы.
        DatabasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    /// <inheritdoc />
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        ApplyPragmas(connection);

        return connection;
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        // Внешние ключи в SQLite по умолчанию выключены — включаем на каждом соединении.
        using var fk = connection.CreateCommand();
        fk.CommandText = "PRAGMA foreign_keys = ON;";
        fk.ExecuteNonQuery();

        // WAL-режим позволяет параллельно читать базу во время записи.
        // Режим сохраняется в самом файле базы, но для надёжности включаем явно.
        using var wal = connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode = WAL;";
        wal.ExecuteNonQuery();
    }
}