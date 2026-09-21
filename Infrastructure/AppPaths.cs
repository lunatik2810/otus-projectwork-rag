namespace OtusProjectworkRag.Infrastructure;

/// <summary>
/// Разрешение относительных путей к ресурсам (модель, токенизатор, БД).
/// Ищет файл сначала в рабочем каталоге (dev-запуск), затем в каталоге приложения
/// (publish/single-file). Работает одинаково на Windows и Linux.
/// </summary>
public static class AppPaths
{
    /// <summary>Возвращает полный путь к существующему файлу или путь в каталоге приложения.</summary>
    public static string ResolveFile(string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
        {
            return relativeOrAbsolutePath;
        }

        var candidates = new[]
        {
            Path.GetFullPath(relativeOrAbsolutePath),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativeOrAbsolutePath)),
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[1];
    }

    /// <summary>
    /// Возвращает полный путь к файлу базы данных. Если путь относительный —
    /// предпочитает рабочий каталог (dev-запуск из папки проекта), иначе каталог
    /// приложения. Абсолютные пути (в Docker: /data/vectorDb.db) не меняются.
    /// </summary>
    public static string ResolveDatabasePath(string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
        {
            return relativeOrAbsolutePath;
        }

        var fromWorkingDirectory = Path.GetFullPath(relativeOrAbsolutePath);
        var directory = Path.GetDirectoryName(fromWorkingDirectory);
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
        {
            return fromWorkingDirectory;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativeOrAbsolutePath));
    }

    /// <summary>Возвращает полный путь к каталогу (существующему или создаваемому в каталоге приложения).</summary>
    public static string ResolveDirectory(string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
        {
            return relativeOrAbsolutePath;
        }

        var fromWorkingDirectory = Path.GetFullPath(relativeOrAbsolutePath);
        if (Directory.Exists(fromWorkingDirectory))
        {
            return fromWorkingDirectory;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativeOrAbsolutePath));
    }
}