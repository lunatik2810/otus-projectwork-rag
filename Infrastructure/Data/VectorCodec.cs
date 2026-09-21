namespace OtusProjectworkRag.Infrastructure.Data;

/// <summary>
/// Кодирование/декодирование векторов для хранения в SQLite как BLOB.
/// Формат: float32 little-endian подряд (для размерности 384 — 1536 байт).
/// </summary>
public static class VectorCodec
{
    /// <summary>Преобразует вектор в BLOB (float32 little-endian).</summary>
    public static byte[] ToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Читает вектор из BLOB.</summary>
    public static float[] FromBlob(byte[] blob)
    {
        var vector = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);
        return vector;
    }
}