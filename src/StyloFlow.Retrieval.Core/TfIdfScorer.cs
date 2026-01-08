using System.Text.RegularExpressions;

namespace StyloFlow.Retrieval;

/// <summary>
/// TF-IDF (Term Frequency-Inverse Document Frequency) scoring algorithm.
/// A classic information retrieval algorithm that weighs terms by their
/// frequency in a document balanced against their rarity in the corpus.
/// </summary>
public sealed class TfIdfScorer : IRetrievalScorer
{
    private readonly TfVariant _tfVariant;
    private readonly IdfVariant _idfVariant;
    private TfIdfCorpus? _corpus;

    private static readonly Regex TokenPattern = new(@"\b\w+\b", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
        "by", "from", "as", "is", "was", "are", "were", "been", "be", "have", "has", "had",
        "do", "does", "did", "will", "would", "could", "should", "may", "might", "must",
        "shall", "can", "this", "that", "these", "those", "it", "its", "he", "she", "they",
        "him", "her", "them", "his", "their", "my", "your", "our", "who", "which", "what",
        "when", "where", "why", "how", "all", "each", "every", "both", "few", "more", "most",
        "other", "some", "such", "no", "not", "only", "same", "so", "than", "too", "very",
        "just", "also", "now", "here", "there", "then", "once", "i", "you", "we", "me", "us"
    };

    /// <summary>
    /// Creates a TF-IDF scorer with default variants (log-normalized TF, smooth IDF).
    /// </summary>
    public TfIdfScorer() : this(TfVariant.LogNormalized, IdfVariant.Smooth) { }

    /// <summary>
    /// Creates a TF-IDF scorer with specified variants.
    /// </summary>
    public TfIdfScorer(TfVariant tfVariant, IdfVariant idfVariant)
    {
        _tfVariant = tfVariant;
        _idfVariant = idfVariant;
    }

    /// <summary>
    /// Creates a TF-IDF scorer with a pre-computed corpus.
    /// </summary>
    public TfIdfScorer(TfIdfCorpus corpus, TfVariant tfVariant = TfVariant.LogNormalized,
        IdfVariant idfVariant = IdfVariant.Smooth) : this(tfVariant, idfVariant)
    {
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
    }

    /// <inheritdoc />
    public string Name => "TF-IDF";

    /// <summary>
    /// Build TF-IDF index from document strings.
    /// </summary>
    public void Initialize(IEnumerable<string> documents)
    {
        _corpus = TfIdfCorpus.Build(documents.Select(Tokenize));
    }

    /// <inheritdoc />
    public double Score(IReadOnlyList<string> query, IReadOnlyList<string> document)
    {
        if (query.Count == 0 || document.Count == 0)
            return 0.0;

        var docTermFreqs = CountTerms(document);
        var maxFreq = docTermFreqs.Count > 0 ? docTermFreqs.Values.Max() : 1;
        var totalDocs = _corpus?.DocumentCount ?? 1;

        var score = 0.0;
        foreach (var term in query.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!docTermFreqs.TryGetValue(term, out var rawTf))
                continue;

            var tf = ComputeTf(rawTf, maxFreq, document.Count);
            var docFreq = _corpus?.GetDocumentFrequency(term) ?? 1;
            var idf = ComputeIdf(totalDocs, docFreq);

            score += tf * idf;
        }

        return score;
    }

    /// <summary>
    /// Score raw text document against raw text query.
    /// </summary>
    public double Score(string query, string document)
    {
        return Score(Tokenize(query), Tokenize(document));
    }

    /// <summary>
    /// Compute TF-IDF score for a specific term in a document.
    /// </summary>
    public double ComputeTermTfIdf(string term, string document)
    {
        var tokens = Tokenize(document);
        var termLower = term.ToLowerInvariant();
        var tf = (double)tokens.Count(t => t.Equals(termLower, StringComparison.OrdinalIgnoreCase)) / tokens.Count;

        var df = _corpus?.GetDocumentFrequency(termLower) ?? 1;
        var totalDocs = _corpus?.DocumentCount ?? 1;
        var idf = Math.Log((double)totalDocs / (df + 1));

        return tf * idf;
    }

    /// <summary>
    /// Get the most distinctive terms in a document (highest TF-IDF).
    /// </summary>
    public List<(string Term, double Score)> GetDistinctiveTerms(string document, int topN = 10)
    {
        var tokens = Tokenize(document).Distinct().ToList();

        return tokens
            .Select(t => (Term: t, Score: ComputeTermTfIdf(t, document)))
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .ToList();
    }

    private double ComputeTf(int rawFreq, int maxFreq, int docLength) => _tfVariant switch
    {
        TfVariant.Raw => rawFreq,
        TfVariant.Boolean => rawFreq > 0 ? 1 : 0,
        TfVariant.LogNormalized => 1 + Math.Log(rawFreq),
        TfVariant.DoubleNormalized => 0.5 + 0.5 * rawFreq / maxFreq,
        TfVariant.AugmentedNormalized => 0.5 + 0.5 * rawFreq / maxFreq,
        _ => rawFreq
    };

    private double ComputeIdf(int totalDocs, int docFreq) => _idfVariant switch
    {
        IdfVariant.Standard => Math.Log((double)totalDocs / (docFreq + 1)),
        IdfVariant.Smooth => Math.Log(1 + (double)totalDocs / (docFreq + 1)),
        IdfVariant.Probabilistic => Math.Max(0, Math.Log((totalDocs - docFreq) / (double)(docFreq + 1))),
        _ => Math.Log((double)totalDocs / (docFreq + 1))
    };

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
    /// Tokenize text, removing stop words.
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();

        return TokenPattern.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length > 2 && !StopWords.Contains(t))
            .ToList();
    }
}

/// <summary>
/// Term frequency calculation variants.
/// </summary>
public enum TfVariant
{
    /// <summary>Raw term count.</summary>
    Raw,
    /// <summary>1 if term present, 0 otherwise.</summary>
    Boolean,
    /// <summary>1 + log(tf) - reduces impact of high frequency terms.</summary>
    LogNormalized,
    /// <summary>0.5 + 0.5 * tf/max_tf - normalized to document's max term frequency.</summary>
    DoubleNormalized,
    /// <summary>K + (1-K) * tf/max_tf where K typically 0.5.</summary>
    AugmentedNormalized
}

/// <summary>
/// Inverse document frequency calculation variants.
/// </summary>
public enum IdfVariant
{
    /// <summary>log(N/df) - standard IDF.</summary>
    Standard,
    /// <summary>log(1 + N/df) - smoothed to avoid negative values.</summary>
    Smooth,
    /// <summary>max(0, log((N-df)/df)) - probabilistic IDF.</summary>
    Probabilistic
}

/// <summary>
/// Pre-computed corpus statistics for TF-IDF scoring.
/// </summary>
public sealed class TfIdfCorpus
{
    private readonly Dictionary<string, int> _documentFrequencies;

    /// <summary>
    /// Gets the total number of documents in the corpus.
    /// </summary>
    public int DocumentCount { get; }

    private TfIdfCorpus(int documentCount, Dictionary<string, int> docFreqs)
    {
        DocumentCount = documentCount;
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
    public static TfIdfCorpus Build(IEnumerable<IReadOnlyList<string>> documents)
    {
        var docFreqs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var docCount = 0;

        foreach (var doc in documents)
        {
            docCount++;
            var uniqueTerms = new HashSet<string>(doc, StringComparer.OrdinalIgnoreCase);
            foreach (var term in uniqueTerms)
            {
                docFreqs[term] = docFreqs.GetValueOrDefault(term, 0) + 1;
            }
        }

        return new TfIdfCorpus(docCount, docFreqs);
    }
}
