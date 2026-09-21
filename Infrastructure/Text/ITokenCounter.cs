namespace OtusProjectworkRag.Infrastructure.Text;

/// <summary>
/// Счётчик токенов — абстракция над токенизатором модели.
/// Позволяет посчитать число токенов фрагмента один раз и переиспользовать.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Возвращает число токенов текста (без специальных токенов).</summary>
    int CountTokens(string text);
}