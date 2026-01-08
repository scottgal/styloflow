namespace StyloFlow.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion (RRF) algorithm for combining multiple ranked lists.
///
/// RRF score = Σ 1/(k + rank) for each ranking list
///
/// RRF is particularly effective because:
/// - No score normalization needed between different ranking systems
/// - Robust to outliers
/// - Simple and interpretable
/// - Works well for hybrid search (combining dense + sparse)
/// </summary>
public sealed class ReciprocalRankFusion : IRankFusion
{
    private readonly int _k;

    /// <summary>
    /// Creates an RRF instance with the standard k parameter (60).
    /// </summary>
    public ReciprocalRankFusion() : this(60) { }

    /// <summary>
    /// Creates an RRF instance with a custom k parameter.
    /// </summary>
    /// <param name="k">Ranking constant (typical: 60). Higher k smooths out rank differences.</param>
    public ReciprocalRankFusion(int k)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        _k = k;
    }

    /// <inheritdoc />
    public string Name => "RRF";

    /// <inheritdoc />
    public IReadOnlyList<ScoredItem<T>> Fuse<T>(IReadOnlyList<IReadOnlyList<T>> rankedLists) where T : notnull
    {
        var scores = new Dictionary<T, double>();

        foreach (var rankedList in rankedLists)
        {
            for (var rank = 0; rank < rankedList.Count; rank++)
            {
                var item = rankedList[rank];
                var rrfScore = 1.0 / (_k + rank + 1);

                if (scores.TryGetValue(item, out var existing))
                    scores[item] = existing + rrfScore;
                else
                    scores[item] = rrfScore;
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new ScoredItem<T>(kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Fuse multiple ranked lists with custom scoring functions.
    /// </summary>
    /// <typeparam name="T">The type of items being ranked.</typeparam>
    /// <param name="rankedLists">Ranked lists with their original scores.</param>
    /// <returns>Fused results with combined RRF scores.</returns>
    public IReadOnlyList<ScoredItem<T>> FuseWithScores<T>(
        IReadOnlyList<IReadOnlyList<ScoredItem<T>>> rankedLists) where T : notnull
    {
        var scores = new Dictionary<T, double>();

        foreach (var rankedList in rankedLists)
        {
            for (var rank = 0; rank < rankedList.Count; rank++)
            {
                var item = rankedList[rank].Item;
                var rrfScore = 1.0 / (_k + rank + 1);

                if (scores.TryGetValue(item, out var existing))
                    scores[item] = existing + rrfScore;
                else
                    scores[item] = rrfScore;
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new ScoredItem<T>(kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Convenience method to fuse two ranked lists (common hybrid search case).
    /// </summary>
    public IReadOnlyList<ScoredItem<T>> Fuse<T>(IReadOnlyList<T> listA, IReadOnlyList<T> listB) where T : notnull
    {
        return Fuse(new[] { listA, listB });
    }

    /// <summary>
    /// Convenience method to fuse three ranked lists (dense + sparse + salience).
    /// </summary>
    public IReadOnlyList<ScoredItem<T>> Fuse<T>(
        IReadOnlyList<T> listA,
        IReadOnlyList<T> listB,
        IReadOnlyList<T> listC) where T : notnull
    {
        return Fuse(new[] { listA, listB, listC });
    }
}

/// <summary>
/// Extended RRF that combines dense semantic search with BM25 sparse search.
/// Provides a complete hybrid search solution.
/// </summary>
public sealed class HybridRrfSearch<T> where T : notnull
{
    private readonly Bm25Scorer _bm25;
    private readonly ReciprocalRankFusion _rrf;
    private readonly Func<T, string> _textSelector;
    private readonly Func<T, float[]>? _embeddingSelector;

    /// <summary>
    /// Creates a hybrid search with BM25 and optional dense vectors.
    /// </summary>
    /// <param name="textSelector">Function to extract text from items.</param>
    /// <param name="embeddingSelector">Optional function to extract embeddings.</param>
    /// <param name="k1">BM25 k1 parameter.</param>
    /// <param name="b">BM25 b parameter.</param>
    /// <param name="rrfK">RRF k parameter.</param>
    public HybridRrfSearch(
        Func<T, string> textSelector,
        Func<T, float[]>? embeddingSelector = null,
        double k1 = 1.5,
        double b = 0.75,
        int rrfK = 60)
    {
        _textSelector = textSelector ?? throw new ArgumentNullException(nameof(textSelector));
        _embeddingSelector = embeddingSelector;
        _bm25 = new Bm25Scorer(k1, b);
        _rrf = new ReciprocalRankFusion(rrfK);
    }

    /// <summary>
    /// Initialize with corpus for BM25 statistics.
    /// </summary>
    public void Initialize(IEnumerable<T> corpus)
    {
        _bm25.Initialize(corpus.Select(_textSelector));
    }

    /// <summary>
    /// Search using hybrid RRF fusion.
    /// </summary>
    /// <param name="items">Items to search.</param>
    /// <param name="query">Text query.</param>
    /// <param name="queryEmbedding">Optional query embedding for dense search.</param>
    /// <param name="topK">Number of results to return.</param>
    public IReadOnlyList<ScoredItem<T>> Search(
        IEnumerable<T> items,
        string query,
        float[]? queryEmbedding = null,
        int topK = 10)
    {
        var itemList = items.ToList();

        // BM25 sparse search
        var bm25Results = _bm25.ScoreAll(itemList, _textSelector, query);

        // If no embeddings, return BM25 only
        if (queryEmbedding == null || _embeddingSelector == null)
        {
            return bm25Results.Take(topK).ToList();
        }

        // Dense semantic search
        var denseResults = itemList
            .Select(item => new ScoredItem<T>(item, VectorMath.CosineSimilarity(queryEmbedding, _embeddingSelector(item))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        // Fuse with RRF
        var fused = _rrf.FuseWithScores(new[]
        {
            bm25Results.ToArray(),
            denseResults.ToArray()
        });

        return fused.Take(topK).ToList();
    }
}
