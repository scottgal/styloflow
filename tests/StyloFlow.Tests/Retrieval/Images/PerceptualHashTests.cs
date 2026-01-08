using StyloFlow.Retrieval.Images;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Images;

public class PerceptualHashTests
{
    [Fact]
    public void ComputePdqHash_ValidGrid_ReturnsHash()
    {
        // Arrange
        var grid = new double[16, 16];
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
                grid[x, y] = (x + y) / 32.0;

        // Act
        var hash = PerceptualHash.ComputePdqHash(grid);

        // Assert
        Assert.NotEmpty(hash);
    }

    [Fact]
    public void ComputePdqHash_WrongSize_ThrowsArgumentException()
    {
        // Arrange
        var grid = new double[8, 8];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => PerceptualHash.ComputePdqHash(grid));
    }

    [Fact]
    public void ComputePdqHash_IdenticalGrids_ReturnSameHash()
    {
        // Arrange
        var grid1 = new double[16, 16];
        var grid2 = new double[16, 16];
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                grid1[x, y] = (x * y) / 256.0;
                grid2[x, y] = (x * y) / 256.0;
            }
        }

        // Act
        var hash1 = PerceptualHash.ComputePdqHash(grid1);
        var hash2 = PerceptualHash.ComputePdqHash(grid2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeBlockHash_ValidGrid_ReturnsHash()
    {
        // Arrange
        var grid = new double[8, 8];
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                grid[x, y] = (x + y) / 16.0;

        // Act
        var hash = PerceptualHash.ComputeBlockHash(grid);

        // Assert
        Assert.NotEmpty(hash);
        Assert.Equal(16, hash.Length); // 64 bits = 16 hex chars
    }

    [Fact]
    public void ComputeBlockHash_WrongSize_ThrowsArgumentException()
    {
        // Arrange
        var grid = new double[16, 16];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => PerceptualHash.ComputeBlockHash(grid));
    }

    [Fact]
    public void ComputeColorHash_ValidHistograms_ReturnsHash()
    {
        // Arrange
        var r = new double[16];
        var g = new double[16];
        var b = new double[16];
        for (int i = 0; i < 16; i++)
        {
            r[i] = i / 16.0;
            g[i] = (15 - i) / 16.0;
            b[i] = 0.5;
        }

        // Act
        var hash = PerceptualHash.ComputeColorHash(r, g, b);

        // Assert
        Assert.NotEmpty(hash);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void ComputeColorHash_WrongBinCount_ThrowsArgumentException()
    {
        // Arrange
        var r = new double[8];
        var g = new double[16];
        var b = new double[16];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => PerceptualHash.ComputeColorHash(r, g, b));
    }

    [Fact]
    public void ComputeCompositeHash_CombinesHashes()
    {
        // Arrange
        var pdq = "ABCD1234";
        var block = "12345678";
        var color = "DEADBEEF";

        // Act
        var composite = PerceptualHash.ComputeCompositeHash(pdq, block, color);

        // Assert
        Assert.NotEmpty(composite);
        Assert.Equal(64, composite.Length); // SHA256 = 64 hex chars
    }

    [Fact]
    public void HammingDistance_IdenticalHashes_ReturnsZero()
    {
        // Arrange
        var hash = "ABCD1234";

        // Act
        var distance = PerceptualHash.HammingDistance(hash, hash);

        // Assert
        Assert.Equal(0, distance);
    }

    [Fact]
    public void HammingDistance_DifferentHashes_ReturnsPositiveDistance()
    {
        // Arrange
        var hash1 = "00000000";
        var hash2 = "FFFFFFFF";

        // Act
        var distance = PerceptualHash.HammingDistance(hash1, hash2);

        // Assert
        Assert.True(distance > 0);
        Assert.Equal(32, distance); // All 32 bits differ (8 hex chars * 4 bits each)
    }

    [Fact]
    public void HammingDistance_DifferentLengths_ThrowsArgumentException()
    {
        // Arrange
        var hash1 = "ABCD";
        var hash2 = "ABCDEF";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => PerceptualHash.HammingDistance(hash1, hash2));
    }

    [Fact]
    public void HashSimilarity_IdenticalHashes_ReturnsOne()
    {
        // Arrange
        var hash = "ABCD1234";

        // Act
        var similarity = PerceptualHash.HashSimilarity(hash, hash);

        // Assert
        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void HashSimilarity_CompletelyDifferent_ReturnsZero()
    {
        // Arrange
        var hash1 = "00000000";
        var hash2 = "FFFFFFFF";

        // Act
        var similarity = PerceptualHash.HashSimilarity(hash1, hash2);

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void HashSimilarity_PartialMatch_ReturnsBetweenZeroAndOne()
    {
        // Arrange
        var hash1 = "00001111";
        var hash2 = "00002222";

        // Act
        var similarity = PerceptualHash.HashSimilarity(hash1, hash2);

        // Assert
        Assert.True(similarity > 0);
        Assert.True(similarity < 1);
    }
}

public class ImageHistogramTests
{
    [Fact]
    public void Compute_EmptyValues_ReturnsEmptyHistogram()
    {
        // Arrange
        var values = Array.Empty<byte>();

        // Act
        var histogram = ImageHistogram.Compute(values);

        // Assert
        Assert.Equal(16, histogram.Length);
        Assert.All(histogram, v => Assert.Equal(0.0, v));
    }

    [Fact]
    public void Compute_UniformValues_ReturnsUniformHistogram()
    {
        // Arrange
        var values = new byte[256];
        for (int i = 0; i < 256; i++)
            values[i] = (byte)i;

        // Act
        var histogram = ImageHistogram.Compute(values, bins: 16);

        // Assert
        Assert.Equal(16, histogram.Length);
        Assert.All(histogram, v => Assert.True(Math.Abs(v - 1.0 / 16) < 0.01));
    }

    [Fact]
    public void Compute_AllSameValue_ReturnsOneFullBin()
    {
        // Arrange
        var values = Enumerable.Repeat((byte)128, 100).ToArray();

        // Act
        var histogram = ImageHistogram.Compute(values, bins: 16);

        // Assert
        Assert.Equal(16, histogram.Length);
        var sum = histogram.Sum();
        Assert.Equal(1.0, sum, precision: 5);
    }

    [Fact]
    public void Compute_CustomBinCount_ReturnsCorrectSize()
    {
        // Arrange
        var values = new byte[] { 0, 128, 255 };

        // Act
        var histogram8 = ImageHistogram.Compute(values, bins: 8);
        var histogram32 = ImageHistogram.Compute(values, bins: 32);

        // Assert
        Assert.Equal(8, histogram8.Length);
        Assert.Equal(32, histogram32.Length);
    }
}

public class ImageDeduplicatorTests
{
    [Fact]
    public void Constructor_ValidThreshold_CreatesInstance()
    {
        // Act
        var deduplicator = new ImageDeduplicator(5);

        // Assert
        Assert.NotNull(deduplicator);
    }

    [Fact]
    public void Constructor_NegativeThreshold_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDeduplicator(-1));
    }

    [Fact]
    public void Constructor_ThresholdGreaterThan64_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDeduplicator(65));
    }

    [Fact]
    public void FindDuplicates_EmptyCollection_ReturnsEmpty()
    {
        // Arrange
        var deduplicator = new ImageDeduplicator();
        var items = Array.Empty<(string Id, string Hash, long FileSize)>();

        // Act
        var groups = deduplicator.FindDuplicates(items);

        // Assert
        Assert.Empty(groups);
    }

    [Fact]
    public void FindDuplicates_NoDuplicates_ReturnsEmpty()
    {
        // Arrange
        var deduplicator = new ImageDeduplicator(0); // Strict threshold
        var items = new[]
        {
            ("A", "00000000", 100L),
            ("B", "11111111", 200L),
            ("C", "22222222", 300L)
        };

        // Act
        var groups = deduplicator.FindDuplicates(items);

        // Assert
        Assert.Empty(groups);
    }

    [Fact]
    public void FindDuplicates_IdenticalHashes_FindsDuplicates()
    {
        // Arrange
        var deduplicator = new ImageDeduplicator(5);
        var items = new[]
        {
            ("A", "00000000", 100L),
            ("B", "00000000", 200L),
            ("C", "FFFFFFFF", 300L)
        };

        // Act
        var groups = deduplicator.FindDuplicates(items);

        // Assert
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Items.Count);
    }

    [Fact]
    public void FindDuplicates_SortsByFileSize()
    {
        // Arrange
        var deduplicator = new ImageDeduplicator(5);
        var items = new[]
        {
            ("A", "00000000", 300L),
            ("B", "00000000", 100L),
            ("C", "00000000", 200L)
        };

        // Act
        var groups = deduplicator.FindDuplicates(items);

        // Assert
        Assert.Single(groups);
        Assert.Equal("B", groups[0].Items[0].Id); // Smallest file first
    }

    [Fact]
    public void FindSimilar_FindsMatchingHashes()
    {
        // Arrange
        var deduplicator = new ImageDeduplicator(10);
        var queryHash = "00000000";
        var candidates = new[]
        {
            ("A", "00000001", 100L), // Very similar
            ("B", "FFFFFFFF", 200L)  // Very different
        };

        // Act
        var similar = deduplicator.FindSimilar(queryHash, candidates);

        // Assert
        Assert.Single(similar);
        Assert.Equal("A", similar[0].Id);
    }

    [Fact]
    public void CalculateStats_ReturnsCorrectStatistics()
    {
        // Arrange
        var deduplicator = new ImageDeduplicator();
        var groups = new List<DuplicateGroup<string>>
        {
            new DuplicateGroup<string>(
                new List<DuplicateItem<string>>
                {
                    new DuplicateItem<string>("A", "hash1", 100, 0),
                    new DuplicateItem<string>("B", "hash1", 200, 0)
                },
                "hash1")
        };

        // Act
        var stats = deduplicator.CalculateStats(groups);

        // Assert
        Assert.Equal(1, stats.GroupCount);
        Assert.Equal(1, stats.TotalDuplicates);
        Assert.Equal(200, stats.WastedSpace); // 100 + 200 - 100 (keep smallest)
    }
}

public class ColorAnalysisTests
{
    [Fact]
    public void ExtractDominantColors_EmptyPixels_ReturnsEmpty()
    {
        // Arrange
        var pixels = Array.Empty<(byte R, byte G, byte B)>();

        // Act
        var colors = ColorAnalysis.ExtractDominantColors(pixels);

        // Assert
        Assert.Empty(colors);
    }

    [Fact]
    public void ExtractDominantColors_SingleColor_ReturnsThatColor()
    {
        // Arrange
        var pixels = Enumerable.Repeat((R: (byte)255, G: (byte)0, B: (byte)0), 100).ToArray();

        // Act
        var colors = ColorAnalysis.ExtractDominantColors(pixels, k: 1);

        // Assert
        Assert.Single(colors);
        Assert.True(colors[0].R > 200); // Should be close to red
    }

    [Fact]
    public void ExtractDominantColors_OrderedByPercentage()
    {
        // Arrange
        var pixels = new List<(byte R, byte G, byte B)>();
        pixels.AddRange(Enumerable.Repeat((R: (byte)255, G: (byte)0, B: (byte)0), 100));
        pixels.AddRange(Enumerable.Repeat((R: (byte)0, G: (byte)255, B: (byte)0), 50));

        // Act
        var colors = ColorAnalysis.ExtractDominantColors(pixels.ToArray(), k: 2);

        // Assert
        Assert.True(colors[0].Percentage >= colors[1].Percentage);
    }

    [Fact]
    public void CalculateColorTemperature_WarmColors_ReturnsPositive()
    {
        // Arrange
        var colors = new[] { new DominantColor(255, 0, 0, 1.0) }; // Pure red

        // Act
        var temperature = ColorAnalysis.CalculateColorTemperature(colors);

        // Assert
        Assert.True(temperature > 0);
    }

    [Fact]
    public void CalculateColorTemperature_CoolColors_ReturnsNegative()
    {
        // Arrange
        var colors = new[] { new DominantColor(0, 0, 255, 1.0) }; // Pure blue

        // Act
        var temperature = ColorAnalysis.CalculateColorTemperature(colors);

        // Assert
        Assert.True(temperature < 0);
    }

    [Fact]
    public void CalculateColorDiversity_EmptyPixels_ReturnsZero()
    {
        // Arrange
        var pixels = Array.Empty<(byte R, byte G, byte B)>();

        // Act
        var diversity = ColorAnalysis.CalculateColorDiversity(pixels);

        // Assert
        Assert.Equal(0.0, diversity);
    }

    [Fact]
    public void CalculateColorDiversity_SingleColor_ReturnsLow()
    {
        // Arrange
        var pixels = Enumerable.Repeat((R: (byte)128, G: (byte)128, B: (byte)128), 1000).ToArray();

        // Act
        var diversity = ColorAnalysis.CalculateColorDiversity(pixels);

        // Assert
        Assert.True(diversity <= 0.01);
    }

    [Fact]
    public void RgbToHsl_Red_ReturnsCorrectHsl()
    {
        // Act
        var (h, s, l) = ColorAnalysis.RgbToHsl(255, 0, 0);

        // Assert
        Assert.Equal(0.0, h, precision: 1); // Hue 0 for red
        Assert.Equal(1.0, s, precision: 2); // Full saturation
        Assert.Equal(0.5, l, precision: 2); // Middle lightness
    }

    [Fact]
    public void RgbToHsl_Gray_ReturnZeroSaturation()
    {
        // Act
        var (h, s, l) = ColorAnalysis.RgbToHsl(128, 128, 128);

        // Assert
        Assert.Equal(0.0, s, precision: 2); // No saturation for gray
    }
}

public class DominantColorTests
{
    [Fact]
    public void ToHex_ReturnsCorrectFormat()
    {
        // Arrange
        var color = new DominantColor(255, 128, 0, 0.5);

        // Act
        var hex = color.ToHex();

        // Assert
        Assert.Equal("#FF8000", hex);
    }

    [Fact]
    public void ToHsl_ReturnsValidHsl()
    {
        // Arrange
        var color = new DominantColor(255, 0, 0, 1.0);

        // Act
        var (h, s, l) = color.ToHsl();

        // Assert
        Assert.True(h >= 0 && h <= 360);
        Assert.True(s >= 0 && s <= 1);
        Assert.True(l >= 0 && l <= 1);
    }
}
