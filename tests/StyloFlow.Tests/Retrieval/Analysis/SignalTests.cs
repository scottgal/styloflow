using StyloFlow.Retrieval.Analysis;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Analysis;

public class SignalTests
{
    [Fact]
    public void Signal_RequiredProperties_MustBeSet()
    {
        // Arrange & Act
        var signal = new Signal
        {
            Key = "test.signal.key",
            Source = "TestSource"
        };

        // Assert
        Assert.Equal("test.signal.key", signal.Key);
        Assert.Equal("TestSource", signal.Source);
    }

    [Fact]
    public void Signal_DefaultConfidence_IsOne()
    {
        // Arrange & Act
        var signal = new Signal
        {
            Key = "test.key",
            Source = "TestSource"
        };

        // Assert
        Assert.Equal(1.0, signal.Confidence);
    }

    [Fact]
    public void Signal_DefaultTimestamp_IsUtcNow()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var signal = new Signal
        {
            Key = "test.key",
            Source = "TestSource"
        };

        var after = DateTime.UtcNow;

        // Assert
        Assert.True(signal.Timestamp >= before);
        Assert.True(signal.Timestamp <= after);
    }

    [Fact]
    public void Signal_Value_CanBeAnyType()
    {
        // Arrange & Act
        var stringSignal = new Signal { Key = "test", Source = "src", Value = "hello" };
        var intSignal = new Signal { Key = "test", Source = "src", Value = 42 };
        var doubleSignal = new Signal { Key = "test", Source = "src", Value = 3.14 };
        var listSignal = new Signal { Key = "test", Source = "src", Value = new List<int> { 1, 2, 3 } };

        // Assert
        Assert.Equal("hello", stringSignal.Value);
        Assert.Equal(42, intSignal.Value);
        Assert.Equal(3.14, doubleSignal.Value);
        Assert.IsType<List<int>>(listSignal.Value);
    }

    [Fact]
    public void Signal_Value_CanBeNull()
    {
        // Arrange & Act
        var signal = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Value = null
        };

        // Assert
        Assert.Null(signal.Value);
    }

    [Fact]
    public void GetValue_CorrectType_ReturnsValue()
    {
        // Arrange
        var signal = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Value = 42
        };

        // Act
        var value = signal.GetValue<int>();

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void GetValue_WrongType_ReturnsDefault()
    {
        // Arrange
        var signal = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Value = "not an int"
        };

        // Act
        var value = signal.GetValue<int>();

        // Assert
        Assert.Equal(0, value);
    }

    [Fact]
    public void GetValue_NullValue_ReturnsDefault()
    {
        // Arrange
        var signal = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Value = null
        };

        // Act
        var value = signal.GetValue<string>();

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void Signal_Metadata_CanBeSet()
    {
        // Arrange & Act
        var signal = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Metadata = new Dictionary<string, object>
            {
                ["algorithm"] = "BM25",
                ["version"] = 2
            }
        };

        // Assert
        Assert.NotNull(signal.Metadata);
        Assert.Equal("BM25", signal.Metadata["algorithm"]);
        Assert.Equal(2, signal.Metadata["version"]);
    }

    [Fact]
    public void Signal_Tags_CanBeSet()
    {
        // Arrange & Act
        var signal = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Tags = new List<string> { SignalTags.Quality, SignalTags.Content }
        };

        // Assert
        Assert.NotNull(signal.Tags);
        Assert.Contains(SignalTags.Quality, signal.Tags);
        Assert.Contains(SignalTags.Content, signal.Tags);
    }

    [Fact]
    public void Signal_ValueType_CanBeSet()
    {
        // Arrange & Act
        var signal = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Value = 42,
            ValueType = "System.Int32"
        };

        // Assert
        Assert.Equal("System.Int32", signal.ValueType);
    }

    [Fact]
    public void Signal_IsRecord_SupportsEquality()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var signal1 = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Value = 42,
            Confidence = 0.9,
            Timestamp = timestamp
        };

        var signal2 = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Value = 42,
            Confidence = 0.9,
            Timestamp = timestamp
        };

        // Assert
        Assert.Equal(signal1, signal2);
    }

    [Fact]
    public void Signal_IsRecord_SupportsWithExpression()
    {
        // Arrange
        var original = new Signal
        {
            Key = "test.key",
            Source = "TestSource",
            Value = 42,
            Confidence = 0.9
        };

        // Act
        var modified = original with { Confidence = 0.5 };

        // Assert
        Assert.Equal(0.9, original.Confidence);
        Assert.Equal(0.5, modified.Confidence);
        Assert.Equal(original.Key, modified.Key);
        Assert.Equal(original.Source, modified.Source);
        Assert.Equal(original.Value, modified.Value);
    }
}

public class SignalTagsTests
{
    [Fact]
    public void SignalTags_CrossDomain_AreCorrect()
    {
        Assert.Equal("quality", SignalTags.Quality);
        Assert.Equal("identity", SignalTags.Identity);
        Assert.Equal("metadata", SignalTags.Metadata);
        Assert.Equal("content", SignalTags.Content);
        Assert.Equal("structure", SignalTags.Structure);
        Assert.Equal("embedding", SignalTags.Embedding);
    }

    [Fact]
    public void SignalTags_DocumentSpecific_AreCorrect()
    {
        Assert.Equal("semantic", SignalTags.Semantic);
        Assert.Equal("lexical", SignalTags.Lexical);
        Assert.Equal("position", SignalTags.Position);
    }

    [Fact]
    public void SignalTags_ImageSpecific_AreCorrect()
    {
        Assert.Equal("visual", SignalTags.Visual);
        Assert.Equal("color", SignalTags.Color);
        Assert.Equal("forensic", SignalTags.Forensic);
    }

    [Fact]
    public void SignalTags_AudioSpecific_AreCorrect()
    {
        Assert.Equal("acoustic", SignalTags.Acoustic);
        Assert.Equal("speech", SignalTags.Speech);
        Assert.Equal("music", SignalTags.Music);
    }

    [Fact]
    public void SignalTags_VideoSpecific_AreCorrect()
    {
        Assert.Equal("motion", SignalTags.Motion);
        Assert.Equal("temporal", SignalTags.Temporal);
        Assert.Equal("scene", SignalTags.Scene);
    }
}

public class AggregationStrategyTests
{
    [Fact]
    public void AggregationStrategy_HasAllExpectedValues()
    {
        // Assert
        Assert.Equal(5, Enum.GetValues<AggregationStrategy>().Length);
        Assert.True(Enum.IsDefined(AggregationStrategy.HighestConfidence));
        Assert.True(Enum.IsDefined(AggregationStrategy.MostRecent));
        Assert.True(Enum.IsDefined(AggregationStrategy.WeightedAverage));
        Assert.True(Enum.IsDefined(AggregationStrategy.MajorityVote));
        Assert.True(Enum.IsDefined(AggregationStrategy.Collect));
    }
}
