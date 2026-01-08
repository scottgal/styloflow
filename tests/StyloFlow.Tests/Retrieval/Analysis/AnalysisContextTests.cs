using StyloFlow.Retrieval.Analysis;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Analysis;

public class AnalysisContextTests
{
    #region Signal Management Tests

    [Fact]
    public void AddSignal_SingleSignal_CanBeRetrieved()
    {
        // Arrange
        var context = new AnalysisContext();
        var signal = new Signal
        {
            Key = "test.signal",
            Source = "TestSource",
            Value = 42
        };

        // Act
        context.AddSignal(signal);

        // Assert
        var retrieved = context.GetSignals("test.signal").ToList();
        Assert.Single(retrieved);
        Assert.Equal(signal, retrieved[0]);
    }

    [Fact]
    public void AddSignal_MultipleSignalsSameKey_AllCanBeRetrieved()
    {
        // Arrange
        var context = new AnalysisContext();
        var signal1 = new Signal { Key = "test.signal", Source = "Source1", Value = 1, Confidence = 0.8 };
        var signal2 = new Signal { Key = "test.signal", Source = "Source2", Value = 2, Confidence = 0.9 };

        // Act
        context.AddSignal(signal1);
        context.AddSignal(signal2);

        // Assert
        var retrieved = context.GetSignals("test.signal").ToList();
        Assert.Equal(2, retrieved.Count);
    }

    [Fact]
    public void AddSignals_MultipleSignals_AllCanBeRetrieved()
    {
        // Arrange
        var context = new AnalysisContext();
        var signals = new[]
        {
            new Signal { Key = "key1", Source = "src", Value = 1 },
            new Signal { Key = "key2", Source = "src", Value = 2 },
            new Signal { Key = "key1", Source = "src", Value = 3 }
        };

        // Act
        context.AddSignals(signals);

        // Assert
        Assert.Equal(2, context.GetSignals("key1").Count());
        Assert.Single(context.GetSignals("key2"));
    }

    [Fact]
    public void GetSignals_NonExistentKey_ReturnsEmpty()
    {
        // Arrange
        var context = new AnalysisContext();

        // Act
        var signals = context.GetSignals("nonexistent");

        // Assert
        Assert.Empty(signals);
    }

    [Fact]
    public void GetBestSignal_MultipleConfidences_ReturnsHighest()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 1, Confidence = 0.5 });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 2, Confidence = 0.9 });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 3, Confidence = 0.7 });

        // Act
        var best = context.GetBestSignal("test");

        // Assert
        Assert.NotNull(best);
        Assert.Equal(2, best.Value);
        Assert.Equal(0.9, best.Confidence);
    }

    [Fact]
    public void GetBestSignal_NonExistentKey_ReturnsNull()
    {
        // Arrange
        var context = new AnalysisContext();

        // Act
        var best = context.GetBestSignal("nonexistent");

        // Assert
        Assert.Null(best);
    }

    [Fact]
    public void GetValue_CorrectType_ReturnsValue()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = "hello" });

        // Act
        var value = context.GetValue<string>("test");

        // Assert
        Assert.Equal("hello", value);
    }

    [Fact]
    public void GetValue_WrongType_ReturnsDefault()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = "hello" });

        // Act
        var value = context.GetValue<int>("test");

        // Assert
        Assert.Equal(0, value);
    }

    [Fact]
    public void HasSignal_ExistingKey_ReturnsTrue()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src" });

        // Act & Assert
        Assert.True(context.HasSignal("test"));
    }

    [Fact]
    public void HasSignal_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        var context = new AnalysisContext();

        // Act & Assert
        Assert.False(context.HasSignal("nonexistent"));
    }

    [Fact]
    public void GetAllSignals_ReturnsAllSignals()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "key1", Source = "src", Value = 1 });
        context.AddSignal(new Signal { Key = "key2", Source = "src", Value = 2 });
        context.AddSignal(new Signal { Key = "key1", Source = "src", Value = 3 });

        // Act
        var all = context.GetAllSignals().ToList();

        // Assert
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GetSignalsByTag_ReturnsMatchingSignals()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "key1", Source = "src", Tags = new List<string> { "quality" } });
        context.AddSignal(new Signal { Key = "key2", Source = "src", Tags = new List<string> { "content" } });
        context.AddSignal(new Signal { Key = "key3", Source = "src", Tags = new List<string> { "quality", "content" } });

        // Act
        var qualitySignals = context.GetSignalsByTag("quality").ToList();

        // Assert
        Assert.Equal(2, qualitySignals.Count);
    }

    [Fact]
    public void GetSignalsByTag_NoMatchingTags_ReturnsEmpty()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "key1", Source = "src", Tags = new List<string> { "quality" } });

        // Act
        var signals = context.GetSignalsByTag("nonexistent");

        // Assert
        Assert.Empty(signals);
    }

    #endregion

    #region Cache Tests

    [Fact]
    public void SetCached_GetCached_ReturnsValue()
    {
        // Arrange
        var context = new AnalysisContext();

        // Act
        context.SetCached("myKey", "myValue");
        var value = context.GetCached<string>("myKey");

        // Assert
        Assert.Equal("myValue", value);
    }

    [Fact]
    public void GetCached_NonExistentKey_ReturnsDefault()
    {
        // Arrange
        var context = new AnalysisContext();

        // Act
        var value = context.GetCached<string>("nonexistent");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void GetCached_WrongType_ReturnsDefault()
    {
        // Arrange
        var context = new AnalysisContext();
        context.SetCached("myKey", "myValue");

        // Act
        var value = context.GetCached<int>("myKey");

        // Assert
        Assert.Equal(0, value);
    }

    [Fact]
    public void HasCached_ExistingKey_ReturnsTrue()
    {
        // Arrange
        var context = new AnalysisContext();
        context.SetCached("myKey", "myValue");

        // Act & Assert
        Assert.True(context.HasCached("myKey"));
    }

    [Fact]
    public void HasCached_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        var context = new AnalysisContext();

        // Act & Assert
        Assert.False(context.HasCached("nonexistent"));
    }

    [Fact]
    public void ClearCache_RemovesCachedData()
    {
        // Arrange
        var context = new AnalysisContext();
        context.SetCached("key1", "value1");
        context.SetCached("key2", "value2");

        // Act
        context.ClearCache();

        // Assert
        Assert.False(context.HasCached("key1"));
        Assert.False(context.HasCached("key2"));
    }

    #endregion

    #region Route Tests

    [Fact]
    public void SelectedRoute_Default_IsNull()
    {
        // Arrange & Act
        var context = new AnalysisContext();

        // Assert
        Assert.Null(context.SelectedRoute);
    }

    [Fact]
    public void IsFastRoute_WhenFast_ReturnsTrue()
    {
        // Arrange
        var context = new AnalysisContext { SelectedRoute = "fast" };

        // Act & Assert
        Assert.True(context.IsFastRoute);
        Assert.False(context.IsQualityRoute);
    }

    [Fact]
    public void IsQualityRoute_WhenQuality_ReturnsTrue()
    {
        // Arrange
        var context = new AnalysisContext { SelectedRoute = "quality" };

        // Act & Assert
        Assert.True(context.IsQualityRoute);
        Assert.False(context.IsFastRoute);
    }

    [Fact]
    public void IsFastRoute_WhenBalanced_ReturnsFalse()
    {
        // Arrange
        var context = new AnalysisContext { SelectedRoute = "balanced" };

        // Act & Assert
        Assert.False(context.IsFastRoute);
        Assert.False(context.IsQualityRoute);
    }

    #endregion

    #region Wave Skip Tests

    [Fact]
    public void SkipWave_IsWaveSkipped_ReturnsTrue()
    {
        // Arrange
        var context = new AnalysisContext();

        // Act
        context.SkipWave("ExpensiveWave");

        // Assert
        Assert.True(context.IsWaveSkipped("ExpensiveWave"));
    }

    [Fact]
    public void IsWaveSkipped_NotSkipped_ReturnsFalse()
    {
        // Arrange
        var context = new AnalysisContext();

        // Act & Assert
        Assert.False(context.IsWaveSkipped("SomeWave"));
    }

    #endregion

    #region Aggregation Tests

    [Fact]
    public void Aggregate_NoSignals_ReturnsNull()
    {
        // Arrange
        var context = new AnalysisContext();

        // Act
        var result = context.Aggregate("nonexistent", AggregationStrategy.HighestConfidence);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Aggregate_HighestConfidence_ReturnsHighestConfidenceSignal()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src1", Value = 1, Confidence = 0.5 });
        context.AddSignal(new Signal { Key = "test", Source = "src2", Value = 2, Confidence = 0.9 });
        context.AddSignal(new Signal { Key = "test", Source = "src3", Value = 3, Confidence = 0.7 });

        // Act
        var result = context.Aggregate("test", AggregationStrategy.HighestConfidence);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Value);
        Assert.Equal(0.9, result.Confidence);
    }

    [Fact]
    public void Aggregate_MostRecent_ReturnsMostRecentSignal()
    {
        // Arrange
        var context = new AnalysisContext();
        var oldest = DateTime.UtcNow.AddMinutes(-10);
        var middle = DateTime.UtcNow.AddMinutes(-5);
        var newest = DateTime.UtcNow;

        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 1, Timestamp = oldest });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 2, Timestamp = newest });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 3, Timestamp = middle });

        // Act
        var result = context.Aggregate("test", AggregationStrategy.MostRecent);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Value);
    }

    [Fact]
    public void Aggregate_WeightedAverage_ComputesCorrectly()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 10.0, Confidence = 0.5 });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 20.0, Confidence = 0.5 });

        // Act
        var result = context.Aggregate("test", AggregationStrategy.WeightedAverage);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(15.0, (double)result.Value!, precision: 5);
    }

    [Fact]
    public void Aggregate_WeightedAverage_WithDifferentWeights()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 10.0, Confidence = 0.8 });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 30.0, Confidence = 0.2 });

        // Act
        var result = context.Aggregate("test", AggregationStrategy.WeightedAverage);

        // Assert
        Assert.NotNull(result);
        // Weighted average: (10 * 0.8 + 30 * 0.2) / (0.8 + 0.2) = (8 + 6) / 1.0 = 14
        Assert.Equal(14.0, (double)result.Value!, precision: 5);
    }

    [Fact]
    public void Aggregate_WeightedAverage_NonNumeric_FallsBackToHighestConfidence()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = "string1", Confidence = 0.5 });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = "string2", Confidence = 0.9 });

        // Act
        var result = context.Aggregate("test", AggregationStrategy.WeightedAverage);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("string2", result.Value);
    }

    [Fact]
    public void Aggregate_MajorityVote_ReturnsHighestVote()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src1", Value = "A", Confidence = 0.3 });
        context.AddSignal(new Signal { Key = "test", Source = "src2", Value = "B", Confidence = 0.4 });
        context.AddSignal(new Signal { Key = "test", Source = "src3", Value = "B", Confidence = 0.3 });

        // Act
        var result = context.Aggregate("test", AggregationStrategy.MajorityVote);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("B", result.Value);
    }

    [Fact]
    public void Aggregate_Collect_ReturnsAllValues()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 1 });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 2 });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 3 });

        // Act
        var result = context.Aggregate("test", AggregationStrategy.Collect);

        // Assert
        Assert.NotNull(result);
        var values = result.Value as List<object?>;
        Assert.NotNull(values);
        Assert.Equal(3, values.Count);
        Assert.Contains(1, values);
        Assert.Contains(2, values);
        Assert.Contains(3, values);
    }

    [Fact]
    public void Aggregate_Collect_MergesTags()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 1, Tags = new List<string> { "tag1" } });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 2, Tags = new List<string> { "tag2" } });
        context.AddSignal(new Signal { Key = "test", Source = "src", Value = 3, Tags = new List<string> { "tag1", "tag3" } });

        // Act
        var result = context.Aggregate("test", AggregationStrategy.Collect);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Tags);
        Assert.Contains("tag1", result.Tags);
        Assert.Contains("tag2", result.Tags);
        Assert.Contains("tag3", result.Tags);
        Assert.Equal(3, result.Tags.Count); // Distinct tags
    }

    [Fact]
    public void Aggregate_Collect_SourceIsAggregated()
    {
        // Arrange
        var context = new AnalysisContext();
        context.AddSignal(new Signal { Key = "test", Source = "src1", Value = 1 });
        context.AddSignal(new Signal { Key = "test", Source = "src2", Value = 2 });

        // Act
        var result = context.Aggregate("test", AggregationStrategy.Collect);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("aggregated", result.Source);
    }

    #endregion
}

public class WaveResultTests
{
    [Fact]
    public void Success_CreatesSuccessfulResult()
    {
        // Arrange
        var signals = new[]
        {
            new Signal { Key = "key1", Source = "src", Value = 1 },
            new Signal { Key = "key2", Source = "src", Value = 2 }
        };
        var duration = TimeSpan.FromMilliseconds(100);

        // Act
        var result = WaveResult.Success(signals, duration);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(2, result.Signals.Count);
        Assert.Equal(duration, result.Duration);
    }

    [Fact]
    public void Failure_CreatesFailedResult()
    {
        // Arrange
        var error = "Something went wrong";
        var duration = TimeSpan.FromMilliseconds(50);

        // Act
        var result = WaveResult.Failure(error, duration);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
        Assert.Empty(result.Signals);
        Assert.Equal(duration, result.Duration);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new WaveResult();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Empty(result.Signals);
        Assert.Equal(TimeSpan.Zero, result.Duration);
    }
}
