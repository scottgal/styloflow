namespace StyloFlow.Retrieval.Documents;

/// <summary>
/// Maximal Marginal Relevance (MMR) reranker for diversity in retrieval results.
/// Balances relevance to query with diversity among selected documents.
///
/// MMR = λ * sim(doc, query) - (1-λ) * max(sim(doc, selected_docs))
///
/// Use cases:
/// - Avoid redundant search results
/// - Summarization (select diverse representative sentences)
/// - RAG context selection (diverse relevant chunks)
/// </summary>
public sealed class MmrReranker
{
    private readonly double _lambda;

    /// <summary>
    /// Creates an MMR reranker.
    /// </summary>
    /// <param name="lambda">Balance between relevance (1.0) and diversity (0.0). Default 0.7.</param>
    public MmrReranker(double lambda = 0.7)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lambda);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lambda, 1.0);
        _lambda = lambda;
    }

    /// <summary>
    /// Rerank items using MMR to balance relevance and diversity.
    /// </summary>
    /// <typeparam name="T">Type of items.</typeparam>
    /// <param name="items">Items to rerank.</param>
    /// <param name="queryEmbedding">Query embedding for relevance scoring.</param>
    /// <param name="embeddingSelector">Function to get embedding from item.</param>
    /// <param name="topK">Number of items to select.</param>
    /// <returns>Reranked items with MMR scores.</returns>
    public IReadOnlyList<ScoredItem<T>> Rerank<T>(
        IEnumerable<T> items,
        float[] queryEmbedding,
        Func<T, float[]> embeddingSelector,
        int topK) where T : notnull
    {
        var candidates = items.ToList();
        if (candidates.Count == 0 || topK <= 0)
            return Array.Empty<ScoredItem<T>>();

        var selected = new List<ScoredItem<T>>();
        var selectedEmbeddings = new List<float[]>();
        var remaining = new HashSet<int>(Enumerable.Range(0, candidates.Count));

        // Pre-compute relevance scores
        var relevanceScores = candidates
            .Select(item => VectorMath.CosineSimilarity(queryEmbedding, embeddingSelector(item)))
            .ToArray();

        while (selected.Count < topK && remaining.Count > 0)
        {
            var bestIdx = -1;
            var bestMmrScore = double.MinValue;

            foreach (var idx in remaining)
            {
                var relevance = relevanceScores[idx];

                // Diversity penalty: max similarity to already-selected items
                var maxSimToSelected = 0.0;
                if (selectedEmbeddings.Count > 0)
                {
                    var candidateEmbedding = embeddingSelector(candidates[idx]);
                    maxSimToSelected = selectedEmbeddings
                        .Max(sel => VectorMath.CosineSimilarity(candidateEmbedding, sel));
                }

                // MMR score
                var mmrScore = _lambda * relevance - (1 - _lambda) * maxSimToSelected;

                if (mmrScore > bestMmrScore)
                {
                    bestMmrScore = mmrScore;
                    bestIdx = idx;
                }
            }

            if (bestIdx < 0) break;

            selected.Add(new ScoredItem<T>(candidates[bestIdx], bestMmrScore));
            selectedEmbeddings.Add(embeddingSelector(candidates[bestIdx]));
            remaining.Remove(bestIdx);
        }

        return selected;
    }

    /// <summary>
    /// Rerank items using pre-computed similarity scores.
    /// </summary>
    public IReadOnlyList<ScoredItem<T>> Rerank<T>(
        IEnumerable<T> items,
        Func<T, double> relevanceScorer,
        Func<T, T, double> diversityScorer,
        int topK) where T : notnull
    {
        var candidates = items.ToList();
        if (candidates.Count == 0 || topK <= 0)
            return Array.Empty<ScoredItem<T>>();

        var selected = new List<ScoredItem<T>>();
        var remaining = new HashSet<int>(Enumerable.Range(0, candidates.Count));

        while (selected.Count < topK && remaining.Count > 0)
        {
            var bestIdx = -1;
            var bestMmrScore = double.MinValue;

            foreach (var idx in remaining)
            {
                var relevance = relevanceScorer(candidates[idx]);

                var maxSimToSelected = selected.Count > 0
                    ? selected.Max(s => diversityScorer(candidates[idx], s.Item))
                    : 0.0;

                var mmrScore = _lambda * relevance - (1 - _lambda) * maxSimToSelected;

                if (mmrScore > bestMmrScore)
                {
                    bestMmrScore = mmrScore;
                    bestIdx = idx;
                }
            }

            if (bestIdx < 0) break;

            selected.Add(new ScoredItem<T>(candidates[bestIdx], bestMmrScore));
            remaining.Remove(bestIdx);
        }

        return selected;
    }
}
