using StyloFlow.Retrieval;
using Xunit;

namespace StyloFlow.Tests.Retrieval;

public class VectorMathTests
{
    private const double Tolerance = 1e-6;

    #region CosineSimilarity Tests

    [Fact]
    public void CosineSimilarity_FloatArray_IdenticalVectors_ReturnsOne()
    {
        // Arrange
        var a = new float[] { 1, 2, 3 };
        var b = new float[] { 1, 2, 3 };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(1.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_FloatArray_OppositeVectors_ReturnsNegativeOne()
    {
        // Arrange
        var a = new float[] { 1, 0, 0 };
        var b = new float[] { -1, 0, 0 };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(-1.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_FloatArray_OrthogonalVectors_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1, 0, 0 };
        var b = new float[] { 0, 1, 0 };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(0.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_FloatArray_DifferentLengths_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1, 2, 3 };
        var b = new float[] { 1, 2 };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void CosineSimilarity_FloatArray_EmptyArrays_ReturnsZero()
    {
        // Arrange
        var a = Array.Empty<float>();
        var b = Array.Empty<float>();

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void CosineSimilarity_FloatArray_ZeroVector_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 0, 0, 0 };
        var b = new float[] { 1, 2, 3 };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void CosineSimilarity_Span_IdenticalVectors_ReturnsOne()
    {
        // Arrange
        ReadOnlySpan<float> a = new float[] { 1, 2, 3 };
        ReadOnlySpan<float> b = new float[] { 1, 2, 3 };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(1.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_DoubleArray_IdenticalVectors_ReturnsOne()
    {
        // Arrange
        var a = new double[] { 1, 2, 3 };
        var b = new double[] { 1, 2, 3 };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(1.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_DoubleArray_DifferentLengths_ReturnsZero()
    {
        // Arrange
        var a = new double[] { 1, 2 };
        var b = new double[] { 1, 2, 3 };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void CosineSimilarity_LargeVectors_WorksCorrectly()
    {
        // Arrange - Large enough to potentially use SIMD
        var a = Enumerable.Range(1, 1000).Select(x => (float)x).ToArray();
        var b = Enumerable.Range(1, 1000).Select(x => (float)x).ToArray();

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(1.0, similarity, precision: 5);
    }

    #endregion

    #region EuclideanDistance Tests

    [Fact]
    public void EuclideanDistance_IdenticalVectors_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1, 2, 3 };
        var b = new float[] { 1, 2, 3 };

        // Act
        var distance = VectorMath.EuclideanDistance(a, b);

        // Assert
        Assert.Equal(0.0, distance, precision: 5);
    }

    [Fact]
    public void EuclideanDistance_UnitDistance_ReturnsOne()
    {
        // Arrange
        var a = new float[] { 0, 0 };
        var b = new float[] { 1, 0 };

        // Act
        var distance = VectorMath.EuclideanDistance(a, b);

        // Assert
        Assert.Equal(1.0, distance, precision: 5);
    }

    [Fact]
    public void EuclideanDistance_KnownDistance_ReturnsCorrectValue()
    {
        // Arrange - 3-4-5 right triangle
        var a = new float[] { 0, 0 };
        var b = new float[] { 3, 4 };

        // Act
        var distance = VectorMath.EuclideanDistance(a, b);

        // Assert
        Assert.Equal(5.0, distance, precision: 5);
    }

    [Fact]
    public void EuclideanDistance_DifferentLengths_ReturnsMaxValue()
    {
        // Arrange
        var a = new float[] { 1, 2 };
        var b = new float[] { 1, 2, 3 };

        // Act
        var distance = VectorMath.EuclideanDistance(a, b);

        // Assert
        Assert.Equal(double.MaxValue, distance);
    }

    [Fact]
    public void EuclideanDistance_EmptyArrays_ReturnsMaxValue()
    {
        // Arrange
        var a = Array.Empty<float>();
        var b = Array.Empty<float>();

        // Act
        var distance = VectorMath.EuclideanDistance(a, b);

        // Assert
        Assert.Equal(double.MaxValue, distance);
    }

    #endregion

    #region DotProduct Tests

    [Fact]
    public void DotProduct_KnownVectors_ReturnsCorrectValue()
    {
        // Arrange
        var a = new float[] { 1, 2, 3 };
        var b = new float[] { 4, 5, 6 };

        // Act
        var result = VectorMath.DotProduct(a, b);

        // Assert - 1*4 + 2*5 + 3*6 = 4 + 10 + 18 = 32
        Assert.Equal(32f, result);
    }

    [Fact]
    public void DotProduct_OrthogonalVectors_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1, 0 };
        var b = new float[] { 0, 1 };

        // Act
        var result = VectorMath.DotProduct(a, b);

        // Assert
        Assert.Equal(0f, result);
    }

    [Fact]
    public void DotProduct_DifferentLengths_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1, 2 };
        var b = new float[] { 1, 2, 3 };

        // Act
        var result = VectorMath.DotProduct(a, b);

        // Assert
        Assert.Equal(0f, result);
    }

    [Fact]
    public void DotProduct_EmptyArrays_ReturnsZero()
    {
        // Arrange
        var a = Array.Empty<float>();
        var b = Array.Empty<float>();

        // Act
        var result = VectorMath.DotProduct(a, b);

        // Assert
        Assert.Equal(0f, result);
    }

    #endregion

    #region L2Norm Tests

    [Fact]
    public void L2Norm_UnitVector_ReturnsOne()
    {
        // Arrange
        var v = new float[] { 1, 0, 0 };

        // Act
        var norm = VectorMath.L2Norm(v);

        // Assert
        Assert.Equal(1f, norm);
    }

    [Fact]
    public void L2Norm_KnownVector_ReturnsCorrectValue()
    {
        // Arrange - 3-4-5 triangle
        var v = new float[] { 3, 4 };

        // Act
        var norm = VectorMath.L2Norm(v);

        // Assert
        Assert.Equal(5f, norm, precision: 5);
    }

    [Fact]
    public void L2Norm_ZeroVector_ReturnsZero()
    {
        // Arrange
        var v = new float[] { 0, 0, 0 };

        // Act
        var norm = VectorMath.L2Norm(v);

        // Assert
        Assert.Equal(0f, norm);
    }

    #endregion

    #region Normalize Tests

    [Fact]
    public void Normalize_ReturnsUnitVector()
    {
        // Arrange
        var v = new float[] { 3, 4 };

        // Act
        var normalized = VectorMath.Normalize(v);

        // Assert
        var norm = VectorMath.L2Norm(normalized);
        Assert.Equal(1f, norm, precision: 5);
    }

    [Fact]
    public void Normalize_PreservesDirection()
    {
        // Arrange
        var v = new float[] { 2, 0 };

        // Act
        var normalized = VectorMath.Normalize(v);

        // Assert
        Assert.Equal(1f, normalized[0], precision: 5);
        Assert.Equal(0f, normalized[1], precision: 5);
    }

    [Fact]
    public void Normalize_DoesNotModifyOriginal()
    {
        // Arrange
        var v = new float[] { 3, 4 };
        var originalFirst = v[0];

        // Act
        var _ = VectorMath.Normalize(v);

        // Assert
        Assert.Equal(originalFirst, v[0]);
    }

    [Fact]
    public void NormalizeInPlace_ModifiesVector()
    {
        // Arrange
        var v = new float[] { 3, 4 };

        // Act
        VectorMath.NormalizeInPlace(v);

        // Assert
        var norm = VectorMath.L2Norm(v);
        Assert.Equal(1f, norm, precision: 5);
    }

    [Fact]
    public void NormalizeInPlace_ZeroVector_RemainsZero()
    {
        // Arrange
        var v = new float[] { 0, 0, 0 };

        // Act
        VectorMath.NormalizeInPlace(v);

        // Assert
        Assert.All(v, x => Assert.Equal(0f, x));
    }

    #endregion

    #region ComputeCentroid Tests

    [Fact]
    public void ComputeCentroid_SingleVector_ReturnsSameVector()
    {
        // Arrange
        var vectors = new[] { new float[] { 1, 2, 3 } };

        // Act
        var centroid = VectorMath.ComputeCentroid(vectors);

        // Assert
        Assert.Equal(new float[] { 1, 2, 3 }, centroid);
    }

    [Fact]
    public void ComputeCentroid_TwoVectors_ReturnsAverage()
    {
        // Arrange
        var vectors = new[]
        {
            new float[] { 0, 0 },
            new float[] { 2, 4 }
        };

        // Act
        var centroid = VectorMath.ComputeCentroid(vectors);

        // Assert
        Assert.Equal(1f, centroid[0]);
        Assert.Equal(2f, centroid[1]);
    }

    [Fact]
    public void ComputeCentroid_EmptyCollection_ReturnsEmptyArray()
    {
        // Arrange
        var vectors = Array.Empty<float[]>();

        // Act
        var centroid = VectorMath.ComputeCentroid(vectors);

        // Assert
        Assert.Empty(centroid);
    }

    #endregion

    #region WeightedAverage Tests

    [Fact]
    public void WeightedAverage_EqualWeights_ReturnsCentroid()
    {
        // Arrange
        var items = new[]
        {
            (new float[] { 0, 0 }, 1.0),
            (new float[] { 2, 4 }, 1.0)
        };

        // Act
        var result = VectorMath.WeightedAverage(items);

        // Assert
        Assert.Equal(1f, result[0]);
        Assert.Equal(2f, result[1]);
    }

    [Fact]
    public void WeightedAverage_UnequalWeights_ReturnsWeightedResult()
    {
        // Arrange
        var items = new[]
        {
            (new float[] { 0, 0 }, 1.0),
            (new float[] { 4, 4 }, 3.0)
        };

        // Act
        var result = VectorMath.WeightedAverage(items);

        // Assert
        // (0*1 + 4*3) / 4 = 12/4 = 3
        Assert.Equal(3f, result[0], precision: 5);
        Assert.Equal(3f, result[1], precision: 5);
    }

    [Fact]
    public void WeightedAverage_EmptyCollection_ReturnsEmptyArray()
    {
        // Arrange
        var items = Array.Empty<(float[], double)>();

        // Act
        var result = VectorMath.WeightedAverage(items);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region TopKBySimilarity Tests

    [Fact]
    public void TopKBySimilarity_ReturnsMostSimilar()
    {
        // Arrange
        var query = new float[] { 1, 0, 0 };
        var vectors = new[]
        {
            new float[] { 1, 0, 0 },     // Most similar
            new float[] { 0, 1, 0 },     // Orthogonal
            new float[] { 0.7f, 0.7f, 0 } // Somewhat similar
        };

        // Act
        var result = VectorMath.TopKBySimilarity(query, vectors, 2);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Index); // First vector is most similar
    }

    [Fact]
    public void TopKBySimilarity_OrderedBySimilarityDescending()
    {
        // Arrange
        var query = new float[] { 1, 0, 0 };
        var vectors = new[]
        {
            new float[] { 0.5f, 0.5f, 0 },
            new float[] { 1, 0, 0 },
            new float[] { 0, 1, 0 }
        };

        // Act
        var result = VectorMath.TopKBySimilarity(query, vectors, 3);

        // Assert
        for (int i = 1; i < result.Count; i++)
        {
            Assert.True(result[i - 1].Similarity >= result[i].Similarity);
        }
    }

    [Fact]
    public void TopKBySimilarity_KLargerThanCollection_ReturnsAll()
    {
        // Arrange
        var query = new float[] { 1, 0 };
        var vectors = new[] { new float[] { 1, 0 }, new float[] { 0, 1 } };

        // Act
        var result = VectorMath.TopKBySimilarity(query, vectors, 10);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TopKBySimilarity_EmptyVectors_ReturnsEmpty()
    {
        // Arrange
        var query = new float[] { 1, 0 };
        var vectors = Array.Empty<float[]>();

        // Act
        var result = VectorMath.TopKBySimilarity(query, vectors, 5);

        // Assert
        Assert.Empty(result);
    }

    #endregion
}
