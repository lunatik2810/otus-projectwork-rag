using OtusProjectworkRag.Infrastructure.Data.Repositories;

namespace OtusProjectworkRag.Infrastructure.Search;

/// <summary>
/// Reciprocal Rank Fusion (RRF): объединяет несколько ранжированных списков.
/// Итоговая оценка чанка = Σ 1 / (k + rank) по всем спискам, где rank — позиция
/// в списке (начиная с 1). Это позволяет одинаково хорошо учитывать и
/// семантическую близость (векторный поиск), и точное совпадение слов (BM25).
/// </summary>
public static class RrfFusion
{
    /// <summary>
    /// Сливает векторный и BM25 списки, возвращая top-Take идентификаторов чанков
    /// с итоговыми RRF-оценками (отсортированы по убыванию).
    /// </summary>
    public static IReadOnlyList<(int ChunkId, double Score)> Fuse(
        IReadOnlyList<SearchHit> vectorHits,
        IReadOnlyList<FtsHit> bm25Hits,
        int k,
        int take)
    {
        var scores = new Dictionary<int, double>();

        AddList(scores, vectorHits.Select(h => h.ChunkId).ToArray(), k);
        AddList(scores, bm25Hits.Select(h => h.ChunkId).ToArray(), k);

        return scores
            .OrderByDescending(pair => pair.Value)
            .Take(take)
            .Select(pair => (pair.Key, pair.Value))
            .ToArray();
    }

    private static void AddList(Dictionary<int, double> scores, int[] chunkIds, int k)
    {
        for (var i = 0; i < chunkIds.Length; i++)
        {
            // rank — позиция начиная с 1.
            var contribution = 1.0 / (k + i + 1);
            scores[chunkIds[i]] = scores.GetValueOrDefault(chunkIds[i]) + contribution;
        }
    }
}