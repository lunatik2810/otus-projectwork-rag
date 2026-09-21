using System.Text;

namespace OtusProjectworkRag.Infrastructure.Text;

/// <summary>
/// Чтение текстовых файлов с определением кодировки.
/// Логика: сначала BOM (UTF-8/UTF-16), затем строгий UTF-8,
/// при ошибке декодирования — fallback на Windows-1251 (частая кодировка
/// русскоязычных txt). Для работы кодировки 1251 на Linux требуется
/// Encoding.RegisterProvider(CodePagesEncodingProvider.Instance) в Program.cs.
/// </summary>
public static class TextFileReader
{
    /// <summary>Читает весь файл, определяя кодировку автоматически.</summary>
    public static string ReadAllText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        // 1. Определяем кодировку по BOM и декодируем без преамбулы.
        var bomEncoding = DetectBom(bytes);
        if (bomEncoding is not null)
        {
            var preambleLength = bomEncoding.GetPreamble().Length;
            return bomEncoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }

        // 2. Пробуем строгий UTF-8 (без BOM): если байты некорректны — исключение.
        try
        {
            var strictUtf8 = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // 3. Fallback: Windows-1251 (однобайтовая кодировка не выбрасывает
            //    исключений, поэтому подходит как запасной вариант).
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    private static Encoding? DetectBom(byte[] bytes)
    {
        // UTF-8 BOM: EF BB BF
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(true);
        }

        // UTF-16 LE BOM: FF FE
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        // UTF-16 BE BOM: FE FF
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return null;
    }
}