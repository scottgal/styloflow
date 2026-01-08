using System.Text.RegularExpressions;

namespace StyloFlow.Retrieval;

/// <summary>
/// BM25 (Best Matching 25) sparse retrieval scorer.
///
/// Provides lexical/keyword matching to complement dense semantic search.
/// Particularly good for:
/// - Exact term matches (proper nouns, technical terms)
/// - Rare words that embeddings may not capture well
/// - Code snippets and identifiers
///
/// Combined with dense retrieval via RRF for hybrid search.
/// </summary>
/// <remarks>
/// The algorithm uses term frequency (TF) and inverse document frequency (IDF) with
/// saturation for term frequency and document length normalization.
///
/// Formula: score(D,Q) = Σ IDF(qi) * (f(qi,D) * (k1 + 1)) / (f(qi,D) + k1 * (1 - b + b * |D|/avgdl))
/// </remarks>
public sealed class Bm25Scorer : IRetrievalScorer
{
    private readonly double _k1;
    private readonly double _b;
    private Bm25Corpus? _corpus;

    private static readonly Regex TokenPattern = new(@"\b\w+\b", RegexOptions.Compiled);

    /// <summary>
    /// Creates a BM25 scorer with default parameters (k1=1.5, b=0.75).
    /// </summary>
    public Bm25Scorer() : this(1.5, 0.75) { }

    /// <summary>
    /// Creates a BM25 scorer with custom parameters.
    /// </summary>
    /// <param name="k1">Term frequency saturation parameter (typical: 1.2-2.0).</param>
    /// <param name="b">Length normalization parameter (0=no normalization, 1=full normalization, typical: 0.75).</param>
    public Bm25Scorer(double k1, double b)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k1);
        ArgumentOutOfRangeException.ThrowIfNegative(b);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(b, 1.0);

        _k1 = k1;
        _b = b;
    }

    /// <summary>
    /// Creates a BM25 scorer with a pre-computed corpus.
    /// </summary>
    public Bm25Scorer(Bm25Corpus corpus, double k1 = 1.5, double b = 0.75) : this(k1, b)
    {
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
    }

    /// <inheritdoc />
    public string Name => "BM25";

    /// <summary>
    /// Initialize BM25 with corpus statistics from documents.
    /// </summary>
    /// <param name="documents">Documents as raw text strings.</param>
    public void Initialize(IEnumerable<string> documents)
    {
        _corpus = Bm25Corpus.Build(documents.Select(Tokenize));
    }

    /// <inheritdoc />
    public double Score(IReadOnlyList<string> query, IReadOnlyList<string> document)
    {
        if (query.Count == 0 || document.Count == 0)
            return 0.0;

        var docTermFreqs = CountTerms(document);
        var docLength = document.Count;
        var avgDocLength = _corpus?.AverageDocumentLength ?? docLength;
        var totalDocs = _corpus?.DocumentCount ?? 1;

        var score = 0.0;
        foreach (var term in query.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!docTermFreqs.TryGetValue(term, out var tf))
                continue;

            var docFreq = _corpus?.GetDocumentFrequency(term) ?? 1;
            var idf = ComputeIdf(totalDocs, docFreq);
            var tfComponent = (tf * (_k1 + 1)) / (tf + _k1 * (1 - _b + _b * docLength / avgDocLength));

            score += idf * tfComponent;
        }

        return score;
    }

    /// <summary>
    /// Score a raw text document against a raw text query.
    /// </summary>
    public double Score(string query, string document)
    {
        return Score(Tokenize(query), Tokenize(document));
    }

    /// <summary>
    /// Score all documents against a query, returning ranked list.
    /// </summary>
    public List<ScoredItem<T>> ScoreAll<T>(IEnumerable<T> items, Func<T, string> textSelector, string query)
    {
        var queryTokens = Tokenize(query);
        return items
            .Select(item => new ScoredItem<T>(item, Score(queryTokens, Tokenize(textSelector(item)))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    /// <summary>
    /// Computes IDF using the standard BM25 formula with smoothing.
    /// IDF = log((N - df + 0.5) / (df + 0.5) + 1)
    /// </summary>
    private static double ComputeIdf(int totalDocs, int docFreq)
    {
        return Math.Log((totalDocs - docFreq + 0.5) / (docFreq + 0.5) + 1);
    }

    private static Dictionary<string, int> CountTerms(IReadOnlyList<string> terms)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            counts[term] = counts.GetValueOrDefault(term, 0) + 1;
        }
        return counts;
    }

    /// <summary>
    /// Simple tokenization (lowercase, alphanumeric words, skip single chars).
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        return TokenPattern.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length > 1)
            .ToList();
    }
}

/// <summary>
/// Pre-computed corpus statistics for BM25 scoring.
/// </summary>
public sealed class Bm25Corpus
{
    private readonly Dictionary<string, int> _documentFrequencies;

    /// <summary>
    /// Gets the total number of documents in the corpus.
    /// </summary>
    public int DocumentCount { get; }

    /// <summary>
    /// Gets the average document length across the corpus.
    /// </summary>
    public double AverageDocumentLength { get; }

    private Bm25Corpus(int documentCount, double avgLength, Dictionary<string, int> docFreqs)
    {
        DocumentCount = documentCount;
        AverageDocumentLength = avgLength;
        _documentFrequencies = docFreqs;
    }

    /// <summary>
    /// Gets the number of documents containing the given term.
    /// </summary>
    public int GetDocumentFrequency(string term) =>
        _documentFrequencies.GetValueOrDefault(term, 0);

    /// <summary>
    /// Builds corpus statistics from a collection of tokenized documents.
    /// </summary>
    /// <param name="documents">Documents represented as term lists.</param>
    public static Bm25Corpus Build(IEnumerable<IReadOnlyList<string>> documents)
    {
        var docFreqs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalLength = 0L;
        var docCount = 0;

        foreach (var doc in documents)
        {
            docCount++;
            totalLength += doc.Count;

            var uniqueTerms = new HashSet<string>(doc, StringComparer.OrdinalIgnoreCase);
            foreach (var term in uniqueTerms)
            {
                docFreqs[term] = docFreqs.GetValueOrDefault(term, 0) + 1;
            }
        }

        var avgLength = docCount > 0 ? (double)totalLength / docCount : 0;
        return new Bm25Corpus(docCount, avgLength, docFreqs);
    }
}
