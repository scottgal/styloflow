using StyloFlow.WorkflowBuilder.Atoms;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Tests;

public class AtomExecutorRegistryTests
{
    [Fact]
    public void DiscoverAtoms_FindsAllAtoms()
    {
        // Arrange
        var registry = new AtomExecutorRegistry();

        // Act
        registry.DiscoverAtoms(typeof(WorkflowAtomContext).Assembly);

        // Assert
        registry.Count.Should().BeGreaterThan(30, "should discover many atoms");
    }

    [Fact]
    public void DiscoverAtoms_FindsTimerTrigger()
    {
        // Arrange
        var registry = new AtomExecutorRegistry();
        registry.DiscoverAtoms(typeof(WorkflowAtomContext).Assembly);

        // Act
        var found = registry.TryGetExecutor("timer-trigger", out var executor);

        // Assert
        found.Should().BeTrue();
        executor.Should().NotBeNull();
    }

    [Fact]
    public void DiscoverAtoms_FindsSentimentDetector()
    {
        // Arrange
        var registry = new AtomExecutorRegistry();
        registry.DiscoverAtoms(typeof(WorkflowAtomContext).Assembly);

        // Act
        var found = registry.TryGetExecutor("sentiment-detector", out var executor);

        // Assert
        found.Should().BeTrue();
        executor.Should().NotBeNull();
    }

    [Fact]
    public void DiscoverAtoms_FindsBurstDetector()
    {
        // Arrange
        var registry = new AtomExecutorRegistry();
        registry.DiscoverAtoms(typeof(WorkflowAtomContext).Assembly);

        // Act
        var found = registry.TryGetExecutor("burst-detector", out var executor);

        // Assert
        found.Should().BeTrue();
        executor.Should().NotBeNull();
    }

    [Fact]
    public void DiscoverAtoms_FindsMapReduceAtoms()
    {
        // Arrange
        var registry = new AtomExecutorRegistry();
        registry.DiscoverAtoms(typeof(WorkflowAtomContext).Assembly);

        // Act & Assert
        registry.TryGetExecutor("accumulator", out _).Should().BeTrue();
        registry.TryGetExecutor("reducer", out _).Should().BeTrue();
        registry.TryGetExecutor("bm25-scorer", out _).Should().BeTrue();
        registry.TryGetExecutor("rrf-scorer", out _).Should().BeTrue();
        registry.TryGetExecutor("mmr-scorer", out _).Should().BeTrue();
        registry.TryGetExecutor("topk-selector", out _).Should().BeTrue();
        registry.TryGetExecutor("deduplicator", out _).Should().BeTrue();
    }

    [Fact]
    public void DiscoverAtoms_FindsSignalShapers()
    {
        // Arrange
        var registry = new AtomExecutorRegistry();
        registry.DiscoverAtoms(typeof(WorkflowAtomContext).Assembly);

        // Act & Assert
        registry.TryGetExecutor("signal-clamp", out _).Should().BeTrue();
        registry.TryGetExecutor("signal-filter", out _).Should().BeTrue();
        registry.TryGetExecutor("signal-mixer", out _).Should().BeTrue();
        registry.TryGetExecutor("signal-quantizer", out _).Should().BeTrue();
        registry.TryGetExecutor("signal-switch", out _).Should().BeTrue();
    }

    [Fact]
    public void TryGetExecutor_ReturnsFalse_ForUnknownAtom()
    {
        // Arrange
        var registry = new AtomExecutorRegistry();
        registry.DiscoverAtoms(typeof(WorkflowAtomContext).Assembly);

        // Act
        var found = registry.TryGetExecutor("non-existent-atom", out var executor);

        // Assert
        found.Should().BeFalse();
        executor.Should().BeNull();
    }

    [Fact]
    public void Register_AddsCustomExecutor()
    {
        // Arrange
        var registry = new AtomExecutorRegistry();
        static Task CustomExecutor(WorkflowAtomContext ctx) => Task.CompletedTask;

        // Act
        registry.Register("custom-atom", CustomExecutor);
        var found = registry.TryGetExecutor("custom-atom", out var executor);

        // Assert
        found.Should().BeTrue();
        executor.Should().Be((Func<WorkflowAtomContext, Task>)CustomExecutor);
    }

    [Fact]
    public void GetContract_ReturnsContract_WhenAvailable()
    {
        // Arrange
        var registry = new AtomExecutorRegistry();
        registry.DiscoverAtoms(typeof(WorkflowAtomContext).Assembly);

        // Act
        var contract = registry.GetContract("timer-trigger");

        // Assert
        contract.Should().NotBeNull();
        contract!.Name.Should().Be("timer-trigger");
    }
}
