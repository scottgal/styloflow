using Mostlylucid.Ephemeral;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Tests;

public class WorkflowSignalsTests
{
    [Fact]
    public void Emit_StoresSignalValue()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");

        // Act
        signals.Emit("test.signal", "hello", "node1");

        // Assert
        signals.Has("test.signal").Should().BeTrue();
    }

    [Fact]
    public void Get_ReturnsEmittedValue()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");
        signals.Emit("test.value", 42.5, "node1");

        // Act
        var value = signals.Get<double>("test.value");

        // Assert
        value.Should().Be(42.5);
    }

    [Fact]
    public void Get_ReturnsDefault_WhenSignalNotFound()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");

        // Act
        var value = signals.Get<string>("missing.signal");

        // Assert
        value.Should().BeNull();
    }

    [Fact]
    public void Has_ReturnsFalse_WhenSignalNotEmitted()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");

        // Act & Assert
        signals.Has("missing.signal").Should().BeFalse();
    }

    [Fact]
    public void GetAll_ReturnsAllSignals()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");
        signals.Emit("signal.a", "a", "node1");
        signals.Emit("signal.b", "b", "node2");
        signals.Emit("signal.c", "c", "node3");

        // Act
        var all = signals.GetAll();

        // Assert
        all.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void GetRunSignals_FiltersToCurrentRun()
    {
        // Arrange
        var sink = new SignalSink(maxCapacity: 100, maxAge: TimeSpan.FromMinutes(5));
        using var signals1 = new WorkflowSignals("run-1", sink);
        using var signals2 = new WorkflowSignals("run-2", sink);

        signals1.Emit("signal.a", "from-run-1", "node1");
        signals2.Emit("signal.b", "from-run-2", "node1");

        // Act
        var run1Signals = signals1.GetRunSignals();
        var run2Signals = signals2.GetRunSignals();

        // Assert
        run1Signals.Should().HaveCount(1);
        run2Signals.Should().HaveCount(1);
    }

    [Fact]
    public void Subscribe_ReceivesEmittedSignals()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");
        var received = new List<SignalEvent>();

        signals.Subscribe(e => received.Add(e));

        // Act
        signals.Emit("test.signal", "value", "node1");

        // Assert
        received.Should().ContainSingle();
    }

    [Fact]
    public void WindowAdd_AddsToWindow()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");

        // Act
        signals.WindowAdd("test-window", "key1", new { Value = 1 });
        signals.WindowAdd("test-window", "key2", new { Value = 2 });

        var entries = signals.WindowQuery("test-window");

        // Assert
        entries.Should().HaveCount(2);
    }

    [Fact]
    public void WindowQuery_ReturnsEmptyForNonExistentWindow()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");

        // Act
        var entries = signals.WindowQuery("non-existent");

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public void MarkProcessed_TracksProcessedKeys()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");

        // Act
        signals.MarkProcessed("key1");

        // Assert
        signals.IsProcessed("key1").Should().BeTrue();
        signals.IsProcessed("key2").Should().BeFalse();
    }

    [Fact]
    public void GetUnprocessed_FiltersProcessedKeys()
    {
        // Arrange
        using var signals = new WorkflowSignals("test-run");
        signals.WindowAdd("window", "key1", new { Value = 1 });
        signals.WindowAdd("window", "key2", new { Value = 2 });
        signals.WindowAdd("window", "key3", new { Value = 3 });
        signals.MarkProcessed("key2");

        // Act
        var unprocessed = signals.GetUnprocessed("window");

        // Assert
        unprocessed.Should().HaveCount(2);
        unprocessed.Select(e => e.Key).Should().Contain("key1");
        unprocessed.Select(e => e.Key).Should().Contain("key3");
        unprocessed.Select(e => e.Key).Should().NotContain("key2");
    }
}
