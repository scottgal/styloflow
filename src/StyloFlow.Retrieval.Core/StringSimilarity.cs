namespace StyloFlow.Retrieval;

/// <summary>
/// String similarity algorithms for text comparison, entity matching, and deduplication.
/// AOT-compatible implementations that don't require external dependencies.
/// </summary>
public static class StringSimilarity
{
    /// <summary>
    /// Compute combined similarity using multiple algorithms for robustness.
    /// Weighted average: Jaro-Winkler (50%), Levenshtein (30%), Cosine n-gram (20%).
    /// </summary>
    public static double ComputeCombinedSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;

        var normA = NormalizeForComparison(a);
        var normB = NormalizeForComparison(b);

        if (normA == normB) return 1.0;

        // Containment check
        if (normA.Contains(normB) || normB.Contains(normA))
            return 0.9;

        var jw = JaroWinklerSimilarity(normA, normB);
        var lev = NormalizedLevenshteinSimilarity(normA, normB);
        var cos = CosineNGramSimilarity(normA, normB);

        return (jw * 0.5) + (lev * 0.3) + (cos * 0.2);
    }

    /// <summary>
    /// Jaro-Winkler similarity - good for names, emphasizes prefix matching.
    /// Returns value between 0 and 1.
    /// </summary>
    public static double JaroWinklerSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return s1 == s2 ? 1.0 : 0.0;

        var jaro = JaroSimilarity(s1, s2);

        // Calculate common prefix (up to 4 chars)
        var prefixLength = 0;
        var maxPrefix = Math.Min(4, Math.Min(s1.Length, s2.Length));
        for (int i = 0; i < maxPrefix; i++)
        {
            if (char.ToLowerInvariant(s1[i]) == char.ToLowerInvariant(s2[i]))
                prefixLength++;
            else
                break;
        }

        // Winkler modification: boost for common prefix
        const double scalingFactor = 0.1;
        return jaro + (prefixLength * scalingFactor * (1 - jaro));
    }

    /// <summary>
    /// Jaro similarity base calculation.
    /// </summary>
    public static double JaroSimilarity(string s1, string s2)
    {
        if (s1 == s2) return 1.0;
        if (s1.Length == 0 || s2.Length == 0) return 0.0;

        var matchDistance = Math.Max(s1.Length, s2.Length) / 2 - 1;
        if (matchDistance < 0) matchDistance = 0;

        var s1Matches = new bool[s1.Length];
        var s2Matches = new bool[s2.Length];

        var matches = 0;
        var transpositions = 0;

        // Find matches
        for (int i = 0; i < s1.Length; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, s2.Length);

            for (int j = start; j < end; j++)
            {
                if (s2Matches[j] || char.ToLowerInvariant(s1[i]) != char.ToLowerInvariant(s2[j]))
                    continue;

                s1Matches[i] = true;
                s2Matches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0.0;

        // Count transpositions
        var k = 0;
        for (int i = 0; i < s1.Length; i++)
        {
            if (!s1Matches[i]) continue;
            while (!s2Matches[k]) k++;
            if (char.ToLowerInvariant(s1[i]) != char.ToLowerInvariant(s2[k]))
                transpositions++;
            k++;
        }

        return ((double)matches / s1.Length +
                (double)matches / s2.Length +
                (double)(matches - transpositions / 2) / matches) / 3.0;
    }

    /// <summary>
    /// Normalized Levenshtein similarity (0-1 range).
    /// 1 = identical, 0 = completely different.
    /// </summary>
    public static double NormalizedLevenshteinSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

        var distance = LevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);

        return 1.0 - ((double)distance / maxLength);
    }

    /// <summary>
    /// Levenshtein edit distance - minimum edits to transform s1 into s2.
    /// Uses two-row optimization for memory efficiency.
    /// </summary>
    public static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;

        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (int j = 0; j <= n; j++)
            prev[j] = j;

        for (int i = 1; i <= m; i++)
        {
            curr[0] = i;

            for (int j = 1; j <= n; j++)
            {
                var cost = char.ToLowerInvariant(s1[i - 1]) == char.ToLowerInvariant(s2[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(prev[j] + 1, curr[j - 1] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }

    /// <summary>
    /// Cosine similarity using character n-grams.
    /// Good for detecting similar strings with different word order.
    /// </summary>
    public static double CosineNGramSimilarity(string s1, string s2, int ngramSize = 2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
        if (s1.Length < ngramSize || s2.Length < ngramSize)
            return s1.Equals(s2, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;

        var ngrams1 = GetNGramFrequency(s1, ngramSize);
        var ngrams2 = GetNGramFrequency(s2, ngramSize);

        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;

        foreach (var kv in ngrams1)
        {
            magnitude1 += kv.Value * kv.Value;
            if (ngrams2.TryGetValue(kv.Key, out var count2))
                dotProduct += kv.Value * count2;
        }

        foreach (var kv in ngrams2)
            magnitude2 += kv.Value * kv.Value;

        var denominator = Math.Sqrt(magnitude1) * Math.Sqrt(magnitude2);
        return denominator == 0 ? 0.0 : dotProduct / denominator;
    }

    /// <summary>
    /// Jaccard similarity between two strings (as n-gram sets).
    /// </summary>
    public static double JaccardSimilarity(string s1, string s2, int ngramSize = 2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

        var set1 = ExtractNGrams(s1, ngramSize);
        var set2 = ExtractNGrams(s2, ngramSize);

        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    /// <summary>
    /// Extract character n-grams from a string.
    /// </summary>
    public static HashSet<string> ExtractNGrams(string text, int n = 2)
    {
        var ngrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = text.ToLowerInvariant();

        if (normalized.Length < n) return ngrams;

        for (int i = 0; i <= normalized.Length - n; i++)
        {
            ngrams.Add(normalized.Substring(i, n));
        }

        return ngrams;
    }

    /// <summary>
    /// Get n-gram frequency map for a string.
    /// </summary>
    private static Dictionary<string, int> GetNGramFrequency(string s, int n)
    {
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i <= s.Length - n; i++)
        {
            var ngram = s.Substring(i, n);
            if (!freq.TryAdd(ngram, 1))
                freq[ngram]++;
        }

        return freq;
    }

    /// <summary>
    /// Normalize text for comparison (lowercase, remove punctuation).
    /// </summary>
    public static string NormalizeForComparison(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var chars = new char[text.Length];
        var index = 0;

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
                chars[index++] = char.ToLowerInvariant(c);
            else if (char.IsWhiteSpace(c) && index > 0 && chars[index - 1] != ' ')
                chars[index++] = ' ';
        }

        return new string(chars, 0, index).Trim();
    }
}
