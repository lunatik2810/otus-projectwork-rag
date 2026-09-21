using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OtusProjectworkRag.Configuration;

namespace OtusProjectworkRag.Infrastructure.Text;

/// <summary>
/// Сканирование папки с документами по glob-паттерну и вычисление SHA-256 хеша
/// содержимого файла (по нему определяется изменение файла).
/// </summary>
public interface IFileScanner
{
    /// <summary>
    /// Возвращает полные пути файлов из папки, соответствующих паттерну.
    /// Если паттерн не передан — используется значение из конфигурации (по умолчанию *.txt).
    /// </summary>
    IReadOnlyList<string> Scan(string folderPath, string? globPattern);

    /// <summary>Вычисляет SHA-256 хеш содержимого файла (hex, нижний регистр).</summary>
    string ComputeContentHash(string filePath);
}

/// <inheritdoc cref="IFileScanner" />
public sealed class FileScanner : IFileScanner
{
    private readonly IndexingOptions _options;

    public FileScanner(IOptions<IndexingOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<string> Scan(string folderPath, string? globPattern)
    {
        var pattern = string.IsNullOrWhiteSpace(globPattern)
            ? _options.DefaultGlob
            : globPattern.Trim();

        var root = Path.GetFullPath(folderPath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        // Если паттерн содержит разделители пути — сопоставляем с относительным путём,
        // иначе — только с именем файла.
        var matchAgainstRelativePath = pattern.Contains('/') || pattern.Contains('\\');
        var regex = GlobMatcher.ToRegex(pattern);

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => (Path: file, Match: matchAgainstRelativePath
                ? Path.GetRelativePath(root, file).Replace('\\', '/')
                : Path.GetFileName(file)))
            .Where(x => regex.IsMatch(x.Match))
            .Select(x => x.Path)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    public string ComputeContentHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }
}

/// <summary>
/// Преобразует glob-паттерн (поддерживает *, ?, **) в регулярное выражение.
/// </summary>
internal static class GlobMatcher
{
    public static Regex ToRegex(string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var sb = new System.Text.StringBuilder("^");

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];

            switch (c)
            {
                case '*':
                    if (i + 1 < normalized.Length && normalized[i + 1] == '*')
                    {
                        // "**" — любая последовательность символов, включая разделители пути.
                        sb.Append(".*");
                        i++;

                        // "**/" дополнительно допускает «ноль каталогов»,
                        // чтобы паттерн **/*.txt захватывал и файлы в корне.
                        if (i + 1 < normalized.Length && normalized[i + 1] == '/')
                        {
                            sb.Append("/?");
                            i++;
                        }
                    }
                    else
                    {
                        // "*" — любая последовательность символов внутри каталога.
                        sb.Append("[^/]*");
                    }

                    break;

                case '?':
                    sb.Append("[^/]");
                    break;

                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        sb.Append('$');

        return new Regex(
            sb.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}