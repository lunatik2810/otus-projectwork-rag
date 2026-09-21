using System.Globalization;

namespace OtusProjectworkRag.Infrastructure.Data;

/// <summary>
/// Вспомогательный класс для хранения дат в SQLite.
/// Даты хранятся как текст ISO-8601 (round-trip формат "O") в UTC.
/// </summary>
public static class SqliteDate
{
    /// <summary>Форматирует дату для сохранения в БД (всегда UTC).</summary>
    public static string Format(DateTime value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Разбирает дату из БД, сохраняя Kind из строки.</summary>
    public static DateTime Parse(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}