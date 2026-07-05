using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StyloFlow.Orchestration;
using Xunit;

namespace StyloFlow.Tests;

/// <summary>
///     Pins <see cref="InitSignalBus"/> semantics + the DI helpers that
///     hang lazy-boot on it. This is the primitive that lets producers
///     write to a sink and, on first raise, boot up a coordinator that
///     was otherwise dormant.
/// </summary>
public class InitSignalBusTests
{
    // ── Core bus ────────────────────────────────────────────────────

    [Fact]
    public void Raise_returns_true_on_first_call_false_thereafter()
    {
        var bus = new InitSignalBus();
        Assert.True(bus.Raise("init.foo"));
        Assert.False(bus.Raise("init.foo"));
        Assert.False(bus.Raise("init.foo"));
    }

    [Fact]
    public void HasFired_reflects_state_without_triggering()
    {
        var bus = new InitSignalBus();

        Assert.False(bus.HasFired("init.foo"));
        bus.Raise("init.foo");
        Assert.True(bus.HasFired("init.foo"));
        Assert.False(bus.HasFired("init.bar"));
    }

    [Fact]
    public void Subscribe_before_raise_invokes_handler_on_raise()
    {
        var bus = new InitSignalBus();
        var invocations = 0;

        using var _ = bus.Subscribe("init.foo", () => invocations++);
        bus.Raise("init.foo");

        Assert.Equal(1, invocations);
    }

    [Fact]
    public void Subscribe_after_raise_invokes_handler_immediately()
    {
        var bus = new InitSignalBus();
        bus.Raise("init.foo");
        var invocations = 0;

        using var _ = bus.Subscribe("init.foo", () => invocations++);

        Assert.Equal(1, invocations);
    }

    [Fact]
    public void Multiple_handlers_all_run_once_per_signal()
    {
        var bus = new InitSignalBus();
        var handlerA = 0;
        var handlerB = 0;

        using var _1 = bus.Subscribe("init.foo", () => handlerA++);
        using var _2 = bus.Subscribe("init.foo", () => handlerB++);
        bus.Raise("init.foo");
        bus.Raise("init.foo");

        Assert.Equal(1, handlerA);
        Assert.Equal(1, handlerB);
    }

    [Fact]
    public void Dispose_unsubscribes_before_raise()
    {
        var bus = new InitSignalBus();
        var invocations = 0;

        var subscription = bus.Subscribe("init.foo", () => invocations++);
        subscription.Dispose();
        bus.Raise("init.foo");

        Assert.Equal(0, invocations);
    }

    [Fact]
    public void Handler_exception_does_not_prevent_other_handlers_from_running()
    {
        var bus = new InitSignalBus();
        var goodRan = 0;

        using var _1 = bus.Subscribe("init.foo", () => throw new InvalidOperationException("bad handler"));
        using var _2 = bus.Subscribe("init.foo", () => goodRan++);
        bus.Raise("init.foo");

        Assert.Equal(1, goodRan);
    }

    // ── DI helpers ──────────────────────────────────────────────────

    [Fact]
    public void AddInitSignalBus_registers_singleton()
    {
        var services = new ServiceCollection();
        services.AddInitSignalBus();
        var sp = services.BuildServiceProvider();

        var a = sp.GetRequiredService<IInitSignalBus>();
        var b = sp.GetRequiredService<IInitSignalBus>();

        Assert.Same(a, b);
        Assert.IsType<InitSignalBus>(a);
    }

    [Fact]
    public async Task AddOnInitSignal_defers_coordinator_construction_until_signal_fires()
    {
        var services = new ServiceCollection();
        services.AddOnInitSignal<TrackingCoordinator>("init.coord");
        var sp = services.BuildServiceProvider();

        // Start the host so the bootstrap subscribes to the bus.
        var hostedServices = sp.GetServices<IHostedService>().ToArray();
        foreach (var hs in hostedServices)
            await hs.StartAsync(CancellationToken.None);

        Assert.Equal(0, TrackingCoordinator.ConstructedCount);

        // Fire the signal.
        sp.GetRequiredService<IInitSignalBus>().Raise("init.coord");

        Assert.Equal(1, TrackingCoordinator.ConstructedCount);

        // Subsequent raises are no-ops per the bus semantics; the
        // singleton stays as-is.
        sp.GetRequiredService<IInitSignalBus>().Raise("init.coord");
        Assert.Equal(1, TrackingCoordinator.ConstructedCount);

        foreach (var hs in hostedServices)
            await hs.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AddOnInitSignal_resolves_existing_singleton_when_signal_already_fired_at_start()
    {
        var services = new ServiceCollection();
        services.AddOnInitSignal<TrackingCoordinator>("init.coord");
        var sp = services.BuildServiceProvider();

        // Fire the signal BEFORE the hosted service starts. Simulates a
        // producer that raced boot -- the subscription must still run the
        // resolver immediately on Subscribe.
        sp.GetRequiredService<IInitSignalBus>().Raise("init.coord");
        Assert.Equal(0, TrackingCoordinator.ConstructedCount);

        var hostedServices = sp.GetServices<IHostedService>().ToArray();
        foreach (var hs in hostedServices)
            await hs.StartAsync(CancellationToken.None);

        Assert.Equal(1, TrackingCoordinator.ConstructedCount);

        foreach (var hs in hostedServices)
            await hs.StopAsync(CancellationToken.None);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private sealed class TrackingCoordinator
    {
        private static int s_constructedCount;
        public static int ConstructedCount => s_constructedCount;

        public TrackingCoordinator()
        {
            Interlocked.Increment(ref s_constructedCount);
        }

        public static void Reset() => Interlocked.Exchange(ref s_constructedCount, 0);
    }

    public InitSignalBusTests()
    {
        TrackingCoordinator.Reset();
    }
}