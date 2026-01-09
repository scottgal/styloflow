using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Tests;

public class SignalWindowTests
{
    [Fact]
    public void Add_StoresEntry()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));

        // Act
        window.Add("key1", new { Value = 1 });

        // Assert
        window.Contains("key1").Should().BeTrue();
    }

    [Fact]
    public void Add_UpdatesExistingEntry()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));
        window.Add("key1", new { Value = 1 });

        // Act
        window.Add("key1", new { Value = 2 });
        var entries = window.Query();

        // Assert
        entries.Should().HaveCount(1);
    }

    [Fact]
    public void Add_EvictsOldest_WhenOverCapacity()
    {
        // Arrange
        var window = new SignalWindow("test", 3, TimeSpan.FromMinutes(10));
        window.Add("key1", new { Value = 1 });
        window.Add("key2", new { Value = 2 });
        window.Add("key3", new { Value = 3 });

        // Act
        window.Add("key4", new { Value = 4 });
        var entries = window.Query();

        // Assert
        entries.Should().HaveCount(3);
        window.Contains("key1").Should().BeFalse(); // Evicted
        window.Contains("key4").Should().BeTrue();
    }

    [Fact]
    public void Get_ReturnsEntry()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));
        window.Add("key1", new { Value = 42 });

        // Act
        var entry = window.Get("key1");

        // Assert
        entry.Should().NotBeNull();
        entry!.Key.Should().Be("key1");
    }

    [Fact]
    public void Get_ReturnsNull_ForMissingKey()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));

        // Act
        var entry = window.Get("missing");

        // Assert
        entry.Should().BeNull();
    }

    [Fact]
    public void Query_ReturnsAllEntries()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));
        window.Add("key1", new { Value = 1 });
        window.Add("key2", new { Value = 2 });
        window.Add("key3", new { Value = 3 });

        // Act
        var entries = window.Query();

        // Assert
        entries.Should().HaveCount(3);
    }

    [Fact]
    public void Query_WithLimit_ReturnsLimitedEntries()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));
        for (int i = 0; i < 10; i++)
        {
            window.Add($"key{i}", new { Value = i });
        }

        // Act
        var entries = window.Query(limit: 5);

        // Assert
        entries.Should().HaveCount(5);
    }

    [Fact]
    public void Sample_ReturnsRandomSubset()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));
        for (int i = 0; i < 20; i++)
        {
            window.Add($"key{i}", new { Value = i });
        }

        // Act
        var sample = window.Sample(5);

        // Assert
        sample.Should().HaveCount(5);
        sample.Should().OnlyContain(e => e.Key.StartsWith("key"));
    }

    [Fact]
    public void Sample_ReturnsAllEntries_WhenCountExceedsWindow()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));
        window.Add("key1", new { Value = 1 });
        window.Add("key2", new { Value = 2 });

        // Act
        var sample = window.Sample(10);

        // Assert
        sample.Should().HaveCount(2);
    }

    [Fact]
    public void GetStats_ReturnsCorrectCount()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));
        window.Add("key1", new { Value = 1 });
        window.Add("key2", new { Value = 2 });
        window.Add("key3", new { Value = 3 });

        // Act
        var stats = window.GetStats();

        // Assert
        stats.Count.Should().Be(3);
        stats.WindowName.Should().Be("test");
    }

    [Fact]
    public void GetStats_ReturnsEmpty_ForEmptyWindow()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));

        // Act
        var stats = window.GetStats();

        // Assert
        stats.Count.Should().Be(0);
        stats.OldestEntry.Should().BeNull();
        stats.NewestEntry.Should().BeNull();
    }

    [Fact]
    public void DetectPatterns_DetectsBurst()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));

        // Add entries in rapid succession (simulating burst)
        for (int i = 0; i < 10; i++)
        {
            window.Add($"key{i}", new { Value = i });
        }

        // Act
        var patterns = window.DetectPatterns(PatternType.Burst);

        // Assert
        patterns.Should().NotBeEmpty();
        patterns.Should().Contain(p => p.Type == PatternType.Burst);
    }

    [Fact]
    public void DetectPatterns_ReturnsEmpty_ForInsufficientData()
    {
        // Arrange
        var window = new SignalWindow("test", 100, TimeSpan.FromMinutes(10));
        window.Add("key1", new { Value = 1 });
        window.Add("key2", new { Value = 2 });

        // Act
        var patterns = window.DetectPatterns(PatternType.Burst);

        // Assert
        patterns.Should().BeEmpty();
    }
}
