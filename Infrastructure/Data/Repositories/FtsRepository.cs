using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OtusProjectworkRag.Infrastructure.Data.Repositories;

/// <summary>Результат полнотекстового поиска: чанк и его BM25-оценка.</summary>
public sealed record FtsHit(int ChunkId, double Score);

/// <summary>
/// Репозиторий полнотекстового поиска по виртуальной таблице ChunksFts (BM25).
/// Поиск выполняется через оператор MATCH; запрос обязательно экранируется,
/// иначе спецсимволы FTS5 вызовут исключение.
/// </summary>
public interface IFtsRepository
{
    /// <summary>Возвращает top-K чанков, ранжированных по BM25 (Score выше = лучше).</summary>
    IReadOnlyList<FtsHit> SearchTop(
        SqliteConnection connection, SqliteTransaction? transaction, string query, int topK);
}

/// <inheritdoc cref="IFtsRepository" />
public sealed class FtsRepository : IFtsRepository
{
    // Токен = последовательность букв и цифр. Разделители (пробелы, пунктуация)
    // исключаются, чтобы экранировать спецсимволы синтаксиса FTS5.
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

    public IReadOnlyList<FtsHit> SearchTop(
        SqliteConnection connection, SqliteTransaction? transaction, string query, int topK)
    {
        var ftsQuery = BuildFtsQuery(query);
        if (string.IsNullOrWhiteSpace(ftsQuery))
        {
            return [];
        }

        var result = new List<FtsHit>();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // bm25() возвращает отрицательное значение: чем меньше, тем релевантнее.
        // Инвертируем знак, чтобы Score был «чем больше, тем лучше».
        // Для FTS5-таблицы с внешним контентом (content='Chunks', content_rowid='Id')
        // идентификатор записи доступен как rowid, который совпадает с Chunks.Id.
        command.CommandText = """
            SELECT rowid AS Id, -bm25(ChunksFts) AS Score
            FROM ChunksFts
            WHERE ChunksFts MATCH $query
            ORDER BY Score DESC
            LIMIT $topK;
            """;
        command.Parameters.AddWithValue("$query", ftsQuery);
        command.Parameters.AddWithValue("$topK", topK);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new FtsHit(reader.GetInt32(0), reader.GetDouble(1)));
        }

        return result;
    }

    /// <summary>
    /// Строит безопасный FTS5-запрос: текст разбивается на токены (буквы/цифры),
    /// каждый токен заключается в двойные кавычки — точное совпадение слова.
    /// По умолчанию слова в FTS5 связываются оператором AND (поиск по всем словам).
    /// </summary>
    internal static string BuildFtsQuery(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var tokens = TokenRegex
            .Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal);

        return string.Join(' ', tokens.Select(t => $"\"{t}\""));
    }
}