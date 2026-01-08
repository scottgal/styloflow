using System.Numerics;
using System.Runtime.CompilerServices;

namespace StyloFlow.Retrieval;

/// <summary>
/// Shared vector math utilities for embedding operations.
/// Optimized with SIMD where available.
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Compute cosine similarity between two vectors.
    /// Returns value between -1 and 1, where 1 = identical direction.
    /// </summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>Cosine similarity score.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            return CosineSimilaritySimd(a, b);
        }

        return CosineSimilarityScalar(a, b);
    }

    /// <summary>
    /// Compute cosine similarity between two ReadOnlySpan vectors.
    /// SIMD-optimized when hardware acceleration is available.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            int vectorSize = Vector<float>.Count;
            int i = 0;

            var dotSum = Vector<float>.Zero;
            var normASum = Vector<float>.Zero;
            var normBSum = Vector<float>.Zero;

            for (; i <= a.Length - vectorSize; i += vectorSize)
            {
                var va = new Vector<float>(a.Slice(i, vectorSize));
                var vb = new Vector<float>(b.Slice(i, vectorSize));

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

        // Scalar fallback
        double dotProductScalar = 0;
        double normAScalar = 0;
        double normBScalar = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProductScalar += a[i] * b[i];
            normAScalar += a[i] * a[i];
            normBScalar += b[i] * b[i];
        }

        var denomScalar = Math.Sqrt(normAScalar) * Math.Sqrt(normBScalar);
        return denomScalar == 0 ? 0 : dotProductScalar / denomScalar;
    }

    /// <summary>
    /// Compute cosine similarity between two vectors (double precision).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dotProduct / denom;
    }

    /// <summary>
    /// Scalar implementation for small vectors or when SIMD isn't available.
    /// </summary>
    private static double CosineSimilarityScalar(float[] a, float[] b)
    {
        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dotProduct / denom;
    }

    /// <summary>
    /// SIMD-optimized cosine similarity using System.Numerics.Vector.
    /// Significantly faster for embedding vectors (384+ dimensions).
    /// </summary>
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

    /// <summary>
    /// Compute the Euclidean (L2) distance between two vectors.
    /// SIMD-optimized when hardware acceleration is available.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double EuclideanDistance(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return double.MaxValue;

        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            var aSpan = a.AsSpan();
            var bSpan = b.AsSpan();
            int vectorSize = Vector<float>.Count;
            int i = 0;

            var sumVec = Vector<float>.Zero;

            for (; i <= a.Length - vectorSize; i += vectorSize)
            {
                var va = new Vector<float>(aSpan.Slice(i, vectorSize));
                var vb = new Vector<float>(bSpan.Slice(i, vectorSize));
                var diff = va - vb;
                sumVec += diff * diff;
            }

            float sum = Vector.Sum(sumVec);

            for (; i < a.Length; i++)
            {
                var diff = a[i] - b[i];
                sum += diff * diff;
            }

            return Math.Sqrt(sum);
        }

        // Scalar fallback
        double sumScalar = 0;
        for (int i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sumScalar += diff * diff;
        }

        return Math.Sqrt(sumScalar);
    }

    /// <summary>
    /// Compute the dot product of two vectors.
    /// SIMD-optimized when hardware acceleration is available.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            var aSpan = a.AsSpan();
            var bSpan = b.AsSpan();
            int vectorSize = Vector<float>.Count;
            int i = 0;

            var sumVec = Vector<float>.Zero;

            for (; i <= a.Length - vectorSize; i += vectorSize)
            {
                var va = new Vector<float>(aSpan.Slice(i, vectorSize));
                var vb = new Vector<float>(bSpan.Slice(i, vectorSize));
                sumVec += va * vb;
            }

            float sum = Vector.Sum(sumVec);

            for (; i < a.Length; i++)
            {
                sum += a[i] * b[i];
            }

            return sum;
        }

        // Scalar fallback
        float sumScalar = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sumScalar += a[i] * b[i];
        }
        return sumScalar;
    }

    /// <summary>
    /// Compute the L2 norm (magnitude) of a vector.
    /// SIMD-optimized when hardware acceleration is available.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float L2Norm(float[] v)
    {
        if (v.Length == 0)
            return 0;

        if (Vector.IsHardwareAccelerated && v.Length >= Vector<float>.Count)
        {
            var vSpan = v.AsSpan();
            int vectorSize = Vector<float>.Count;
            int i = 0;

            var sumVec = Vector<float>.Zero;

            for (; i <= v.Length - vectorSize; i += vectorSize)
            {
                var vec = new Vector<float>(vSpan.Slice(i, vectorSize));
                sumVec += vec * vec;
            }

            float sum = Vector.Sum(sumVec);

            for (; i < v.Length; i++)
            {
                sum += v[i] * v[i];
            }

            return MathF.Sqrt(sum);
        }

        // Scalar fallback
        float sumScalar = 0;
        for (int i = 0; i < v.Length; i++)
        {
            sumScalar += v[i] * v[i];
        }
        return MathF.Sqrt(sumScalar);
    }

    /// <summary>
    /// Normalize a vector to unit length (L2 normalization).
    /// Modifies the input array in place.
    /// SIMD-optimized when hardware acceleration is available.
    /// </summary>
    public static void NormalizeInPlace(float[] v)
    {
        float norm = L2Norm(v);
        if (norm <= 0) return;

        if (Vector.IsHardwareAccelerated && v.Length >= Vector<float>.Count)
        {
            var normVec = new Vector<float>(norm);
            var vSpan = v.AsSpan();
            int vectorSize = Vector<float>.Count;
            int i = 0;

            for (; i <= v.Length - vectorSize; i += vectorSize)
            {
                var vec = new Vector<float>(vSpan.Slice(i, vectorSize));
                (vec / normVec).CopyTo(vSpan.Slice(i, vectorSize));
            }

            for (; i < v.Length; i++)
            {
                v[i] /= norm;
            }
        }
        else
        {
            for (int i = 0; i < v.Length; i++)
            {
                v[i] /= norm;
            }
        }
    }

    /// <summary>
    /// Normalize a vector to unit length, returning a new array.
    /// </summary>
    public static float[] Normalize(float[] v)
    {
        var result = new float[v.Length];
        Array.Copy(v, result, v.Length);
        NormalizeInPlace(result);
        return result;
    }

    /// <summary>
    /// Compute the centroid (average) of multiple vectors.
    /// </summary>
    public static float[] ComputeCentroid(IEnumerable<float[]> vectors)
    {
        var vectorList = vectors.ToList();
        if (vectorList.Count == 0)
            return Array.Empty<float>();

        int dim = vectorList[0].Length;
        var centroid = new float[dim];

        foreach (var v in vectorList)
        {
            for (int i = 0; i < dim; i++)
            {
                centroid[i] += v[i];
            }
        }

        int count = vectorList.Count;
        for (int i = 0; i < dim; i++)
        {
            centroid[i] /= count;
        }

        return centroid;
    }

    /// <summary>
    /// Compute weighted average of vectors.
    /// </summary>
    public static float[] WeightedAverage(IEnumerable<(float[] Vector, double Weight)> weightedVectors)
    {
        var items = weightedVectors.ToList();
        if (items.Count == 0)
            return Array.Empty<float>();

        int dim = items[0].Vector.Length;
        var result = new float[dim];
        double totalWeight = 0;

        foreach (var (vector, weight) in items)
        {
            for (int i = 0; i < dim; i++)
            {
                result[i] += (float)(vector[i] * weight);
            }
            totalWeight += weight;
        }

        if (totalWeight > 0)
        {
            for (int i = 0; i < dim; i++)
            {
                result[i] /= (float)totalWeight;
            }
        }

        return result;
    }

    /// <summary>
    /// Find top-K most similar vectors to query using cosine similarity.
    /// </summary>
    public static List<(int Index, double Similarity)> TopKBySimilarity(
        float[] query,
        IReadOnlyList<float[]> vectors,
        int topK)
    {
        var scores = new List<(int Index, double Similarity)>(vectors.Count);

        for (int i = 0; i < vectors.Count; i++)
        {
            var sim = CosineSimilarity(query, vectors[i]);
            scores.Add((i, sim));
        }

        return scores
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .ToList();
    }
}
