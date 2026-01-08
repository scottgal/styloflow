using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// Shared scoring and similarity utilities for map-reduce atoms.
/// SIMD-optimized where applicable. Pure deterministic computation.
/// </summary>
public static class ScoringUtilities
{
    private static readonly Regex TokenPattern = new(@"\b\w+\b", RegexOptions.Compiled);

    #region Vector Math (SIMD-optimized)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
            return CosineSimilaritySimd(a, b);

        return CosineSimilarityScalar(a, b);
    }

    public static double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        double dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dotProduct / denom;
    }

    private static double CosineSimilarityScalar(float[] a, float[] b)
    {
        double dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dotProduct / denom;
    }

    private static double CosineSimilaritySimd(float[] a, float[] b)
    {
        var aSpan = a.AsSpan();
        var bSpan = b.AsSpan();
        int vectorSize = Vector<float>.Count;
        int i = 0;

        var dotSum = Vector<float>.Zero;
        var normASum = Vector<float>.Zero;
        var normBSum = Vector<float>.Zero;

        for (; i <= a.Length - vectorSize; i += vectorSize)
        {
            var va = new Vector<float>(aSpan.Slice(i, vectorSize));
            var vb = new Vector<float>(bSpan.Slice(i, vectorSize));
            dotSum += va * vb;
            normASum += va * va;
            normBSum += vb * vb;
        }

        float dotProduct = Vector.Sum(dotSum);
        float normA = Vector.Sum(normASum);
        float normB = Vector.Sum(normBSum);

        for (; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dotProduct / denom;
    }

    public static float[] ComputeCentroid(IEnumerable<float[]> vectors)
    {
        var vectorList = vectors.ToList();
        if (vectorList.Count == 0) return [];

        int dim = vectorList[0].Length;
        var centroid = new float[dim];

        foreach (var v in vectorList)
            for (int i = 0; i < dim; i++)
                centroid[i] += v[i];

        int count = vectorList.Count;
        for (int i = 0; i < dim; i++)
            centroid[i] /= count;

        return centroid;
    }

    #endregion

    #region String Similarity

    public static double JaroWinklerSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return s1 == s2 ? 1.0 : 0.0;

        var jaro = JaroSimilarity(s1, s2);

        var prefixLength = 0;
        var maxPrefix = Math.Min(4, Math.Min(s1.Length, s2.Length));
        for (int i = 0; i < maxPrefix; i++)
        {
            if (char.ToLowerInvariant(s1[i]) == char.ToLowerInvariant(s2[i]))
                prefixLength++;
            else
                break;
        }

        const double scalingFactor = 0.1;
        return jaro + (prefixLength * scalingFactor * (1 - jaro));
    }

    private static double JaroSimilarity(string s1, string s2)
    {
        if (s1 == s2) return 1.0;
        if (s1.Length == 0 || s2.Length == 0) return 0.0;

        var matchDistance = Math.Max(s1.Length, s2.Length) / 2 - 1;
        if (matchDistance < 0) matchDistance = 0;

        var s1Matches = new bool[s1.Length];
        var s2Matches = new bool[s2.Length];
        var matches = 0;
        var transpositions = 0;

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

    public static double NormalizedLevenshteinSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

        var distance = LevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);
        return 1.0 - ((double)distance / maxLength);
    }

    public static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;

        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (int j = 0; j <= n; j++) prev[j] = j;

        for (int i = 1; i <= m; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= n; j++)
            {
                var cost = char.ToLowerInvariant(s1[i - 1]) == char.ToLowerInvariant(s2[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }

    public static double NGramCosineSimilarity(string s1, string s2, int ngramSize = 2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
        if (s1.Length < ngramSize || s2.Length < ngramSize)
            return s1.Equals(s2, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;

        var ngrams1 = GetNGramFrequency(s1, ngramSize);
        var ngrams2 = GetNGramFrequency(s2, ngramSize);

        double dotProduct = 0, magnitude1 = 0, magnitude2 = 0;

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

    private static Dictionary<string, int> GetNGramFrequency(string s, int n)
    {
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i <= s.Length - n; i++)
        {
            var ngram = s.Substring(i, n);
            freq[ngram] = freq.GetValueOrDefault(ngram) + 1;
        }
        return freq;
    }

    /// <summary>
    /// Combined similarity using weighted Jaro-Winkler, Levenshtein, and n-gram cosine
    /// </summary>
    public static double CombinedStringSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;

        var normA = a.ToLowerInvariant().Trim();
        var normB = b.ToLowerInvariant().Trim();

        if (normA == normB) return 1.0;
        if (normA.Contains(normB) || normB.Contains(normA)) return 0.9;

        var jw = JaroWinklerSimilarity(normA, normB);
        var lev = NormalizedLevenshteinSimilarity(normA, normB);
        var cos = NGramCosineSimilarity(normA, normB);

        return (jw * 0.5) + (lev * 0.3) + (cos * 0.2);
    }

    #endregion

    #region Tokenization

    public static List<string> Tokenize(string text)
    {
        return TokenPattern.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length > 1)
            .ToList();
    }

    public static HashSet<string> TokenizeUnique(string text)
    {
        return TokenPattern.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Ranking

    /// <summary>
    /// RRF (Reciprocal Rank Fusion) score calculation
    /// </summary>
    public static double RRFScore(int rank, double k = 60) => 1.0 / (k + rank);

    /// <summary>
    /// Combine multiple ranked lists using RRF
    /// </summary>
    public static Dictionary<string, double> FuseRankings(
        IEnumerable<IEnumerable<string>> rankedLists,
        double k = 60)
    {
        var scores = new Dictionary<string, double>();

        foreach (var rankedList in rankedLists)
        {
            var rank = 1;
            foreach (var item in rankedList)
            {
                scores[item] = scores.GetValueOrDefault(item) + RRFScore(rank, k);
                rank++;
            }
        }

        return scores;
    }

    #endregion
}
