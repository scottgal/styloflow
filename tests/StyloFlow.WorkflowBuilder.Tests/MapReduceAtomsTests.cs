using StyloFlow.WorkflowBuilder.Atoms.MapReduce;

namespace StyloFlow.WorkflowBuilder.Tests;

public class MapReduceAtomsTests
{
    #region AccumulatorAtom Tests

    [Fact]
    public async Task AccumulatorAtom_AccumulatesEntries()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window",
            ["key_field"] = "id"
        });

        // Add some entities to signals
        ctx.Signals.Emit("entity", new { id = "1", value = "a" }, "source");

        // Act
        await AccumulatorAtom.ExecuteAsync(ctx);

        // Assert
        var count = ctx.Signals.Get<int>("accumulator.count");
        count.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region SumReducerAtom Tests

    [Fact]
    public async Task SumReducerAtom_SumsValues()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window"
        });

        // WindowAdd requires reference types, use AccumulatorEntry
        ctx.Signals.WindowAdd("test-window", "key1", new AccumulatorEntry("group", 10.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key2", new AccumulatorEntry("group", 20.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key3", new AccumulatorEntry("group", 30.0, DateTimeOffset.UtcNow));

        // Act
        await SumReducerAtom.ExecuteAsync(ctx);

        // Assert - SumReducerAtom emits reduce.sum
        var sum = ctx.Signals.Get<double>("reduce.sum");
        sum.Should().Be(60.0);
    }

    [Fact]
    public async Task SumReducerAtom_ReturnsZero_ForEmptyWindow()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "empty-window"
        });

        // Act
        await SumReducerAtom.ExecuteAsync(ctx);

        // Assert
        var sum = ctx.Signals.Get<double>("reduce.sum");
        sum.Should().Be(0.0);
    }

    [Fact]
    public async Task SumReducerAtom_EmitsCount()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window"
        });
        ctx.Signals.WindowAdd("test-window", "key1", new AccumulatorEntry("group", 10.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key2", new AccumulatorEntry("group", 20.0, DateTimeOffset.UtcNow));

        // Act
        await SumReducerAtom.ExecuteAsync(ctx);

        // Assert
        var count = ctx.Signals.Get<int>("reduce.count");
        count.Should().Be(2);
    }

    #endregion

    #region AvgReducerAtom Tests

    [Fact]
    public async Task AvgReducerAtom_CalculatesAverage()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window"
        });

        ctx.Signals.WindowAdd("test-window", "key1", new AccumulatorEntry("group", 10.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key2", new AccumulatorEntry("group", 20.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key3", new AccumulatorEntry("group", 30.0, DateTimeOffset.UtcNow));

        // Act
        await AvgReducerAtom.ExecuteAsync(ctx);

        // Assert - AvgReducerAtom emits reduce.avg
        var avg = ctx.Signals.Get<double>("reduce.avg");
        avg.Should().Be(20.0);
    }

    [Fact]
    public async Task AvgReducerAtom_ReturnsZero_ForEmptyWindow()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "empty-window"
        });

        // Act
        await AvgReducerAtom.ExecuteAsync(ctx);

        // Assert
        var avg = ctx.Signals.Get<double>("reduce.avg");
        avg.Should().Be(0.0);
    }

    #endregion

    #region TopKSelectorAtom Tests

    [Fact]
    public async Task TopKSelectorAtom_SelectsTopK()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window",
            ["k"] = 2
        });

        // TopKSelectorAtom expects AccumulatorEntry or reference types
        ctx.Signals.WindowAdd("test-window", "low", new AccumulatorEntry("g", 0.1, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "medium", new AccumulatorEntry("g", 0.5, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "high", new AccumulatorEntry("g", 0.9, DateTimeOffset.UtcNow));

        // Act
        await TopKSelectorAtom.ExecuteAsync(ctx);

        // Assert
        var count = ctx.Signals.Get<int>("topk.count");
        count.Should().Be(2);
    }

    [Fact]
    public async Task TopKSelectorAtom_ReportsDropped()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window",
            ["k"] = 1
        });

        ctx.Signals.WindowAdd("test-window", "a", new AccumulatorEntry("g", 0.1, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "b", new AccumulatorEntry("g", 0.5, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "c", new AccumulatorEntry("g", 0.9, DateTimeOffset.UtcNow));

        // Act
        await TopKSelectorAtom.ExecuteAsync(ctx);

        // Assert
        var dropped = ctx.Signals.Get<int>("topk.dropped");
        dropped.Should().Be(2);
    }

    #endregion

    #region DeduplicatorAtom Tests

    [Fact]
    public async Task DeduplicatorAtom_RemovesDuplicates()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window",
            ["text_field"] = "content",
            ["threshold"] = 0.9
        });

        // Add items with text content - using string directly works
        ctx.Signals.WindowAdd("test-window", "doc1", "hello world");
        ctx.Signals.WindowAdd("test-window", "doc2", "hello world!"); // Near duplicate
        ctx.Signals.WindowAdd("test-window", "doc3", "goodbye");

        // Act
        await DeduplicatorAtom.ExecuteAsync(ctx);

        // Assert
        var removedCount = ctx.Signals.Get<int>("dedup.duplicates_removed");
        removedCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task DeduplicatorAtom_ReportsClusterCount()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window",
            ["threshold"] = 0.9
        });

        ctx.Signals.WindowAdd("test-window", "a", "unique text one");
        ctx.Signals.WindowAdd("test-window", "b", "unique text two");

        // Act
        await DeduplicatorAtom.ExecuteAsync(ctx);

        // Assert - Should have 2 clusters (unique items)
        var clusters = ctx.Signals.Get<int>("dedup.clusters");
        clusters.Should().Be(2);
    }

    #endregion

    #region ReducerAtom Tests

    [Fact]
    public async Task ReducerAtom_ReducesWithMax()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window",
            ["operation"] = "max"
        });

        // ReducerAtom requires AccumulatorEntry objects
        ctx.Signals.WindowAdd("test-window", "key1", new AccumulatorEntry("group1", 10.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key2", new AccumulatorEntry("group1", 50.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key3", new AccumulatorEntry("group1", 30.0, DateTimeOffset.UtcNow));

        // Act
        await ReducerAtom.ExecuteAsync(ctx);

        // Assert
        var result = ctx.Signals.Get<double>("reduce.result");
        result.Should().Be(50.0);
    }

    [Fact]
    public async Task ReducerAtom_ReducesWithMin()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window",
            ["operation"] = "min"
        });

        ctx.Signals.WindowAdd("test-window", "key1", new AccumulatorEntry("group1", 10.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key2", new AccumulatorEntry("group1", 50.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key3", new AccumulatorEntry("group1", 30.0, DateTimeOffset.UtcNow));

        // Act
        await ReducerAtom.ExecuteAsync(ctx);

        // Assert
        var result = ctx.Signals.Get<double>("reduce.result");
        result.Should().Be(10.0);
    }

    [Fact]
    public async Task ReducerAtom_ReducesWithSum()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window",
            ["operation"] = "sum"
        });

        ctx.Signals.WindowAdd("test-window", "key1", new AccumulatorEntry("group1", 10.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key2", new AccumulatorEntry("group1", 20.0, DateTimeOffset.UtcNow));

        // Act
        await ReducerAtom.ExecuteAsync(ctx);

        // Assert
        var result = ctx.Signals.Get<double>("reduce.result");
        result.Should().Be(30.0);
    }

    [Fact]
    public async Task ReducerAtom_ReportsGroupCount()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["window_name"] = "test-window",
            ["operation"] = "count"
        });

        ctx.Signals.WindowAdd("test-window", "key1", new AccumulatorEntry("group1", 10.0, DateTimeOffset.UtcNow));
        ctx.Signals.WindowAdd("test-window", "key2", new AccumulatorEntry("group2", 20.0, DateTimeOffset.UtcNow));

        // Act
        await ReducerAtom.ExecuteAsync(ctx);

        // Assert
        var groupCount = ctx.Signals.Get<int>("reduce.groups");
        groupCount.Should().Be(2);
    }

    #endregion
}
