namespace StyloFlow.Retrieval;

/// <summary>
/// Interface for retrieval scoring algorithms.
/// </summary>
public interface IRetrievalScorer
{
    /// <summary>
    /// Gets the name of the scoring algorithm.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Scores a document against a query.
    /// </summary>
    /// <param name="query">The query terms.</param>
    /// <param name="document">The document terms.</param>
    /// <returns>A relevance score (higher is more relevant).</returns>
    double Score(IReadOnlyList<string> query, IReadOnlyList<string> document);
}

/// <summary>
/// Interface for fusion algorithms that combine multiple ranked lists.
/// </summary>
public interface IRankFusion
{
    /// <summary>
    /// Gets the name of the fusion algorithm.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Fuses multiple ranked lists into a single ranked list.
    /// </summary>
    /// <typeparam name="T">The type of items being ranked.</typeparam>
    /// <param name="rankedLists">Multiple ranked lists (ordered by relevance, most relevant first).</param>
    /// <returns>A fused ranked list with combined scores.</returns>
    IReadOnlyList<ScoredItem<T>> Fuse<T>(IReadOnlyList<IReadOnlyList<T>> rankedLists) where T : notnull;
}

/// <summary>
/// Represents an item with an associated score.
/// </summary>
/// <typeparam name="T">The type of the item.</typeparam>
public readonly record struct ScoredItem<T>(T Item, double Score) : IComparable<ScoredItem<T>>
{
    public int CompareTo(ScoredItem<T> other) => other.Score.CompareTo(Score); // Descending
}

/// <summary>
/// Interface for vector similarity calculations.
/// </summary>
public interface IVectorSimilarity
{
    /// <summary>
    /// Gets the name of the similarity measure.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Computes similarity between two vectors.
    /// </summary>
    /// <param name="vectorA">First vector.</param>
    /// <param name="vectorB">Second vector.</param>
    /// <returns>Similarity score (typically 0-1 for normalized, or -1 to 1 for cosine).</returns>
    double Compute(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB);
}
