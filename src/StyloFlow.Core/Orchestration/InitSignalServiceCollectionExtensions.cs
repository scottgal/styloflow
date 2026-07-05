using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace StyloFlow.Orchestration;

/// <summary>
///     DI helpers for wiring the <see cref="IInitSignalBus"/> primitive
///     and registering coordinators that lazy-boot on the first raise of a
///     named init signal.
/// </summary>
public static class InitSignalServiceCollectionExtensions
{
    /// <summary>
    ///     Register <see cref="IInitSignalBus"/> + its default
    ///     <see cref="InitSignalBus"/> implementation as a singleton. Safe
    ///     to call multiple times -- uses <c>TryAdd</c> semantics.
    /// </summary>
    public static IServiceCollection AddInitSignalBus(this IServiceCollection services)
    {
        services.TryAddSingleton<IInitSignalBus, InitSignalBus>();
        return services;
    }

    /// <summary>
    ///     Register <typeparamref name="TCoordinator"/> as a singleton whose
    ///     construction is deferred until <paramref name="initSignal"/> is
    ///     raised on <see cref="IInitSignalBus"/>. The host resolves the
    ///     coordinator via
    ///     <see cref="IServiceProvider.GetService(System.Type)"/> at boot
    ///     time on a background <see cref="IHostedService"/>; DI subscribes
    ///     to the init signal and calls that resolver on first raise.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The coordinator is still registered as an ordinary DI
    ///         singleton. Callers can still resolve it eagerly with
    ///         <c>sp.GetService&lt;TCoordinator&gt;()</c> if they want to
    ///         force construction (tests do this). The lazy path is: the
    ///         init-signal bootstrap does the resolve for you, at the right
    ///         moment.
    ///     </para>
    ///     <para>
    ///         Coordinator ctors that subscribe to sinks or start work
    ///         should keep their subscription window tight: the sink they
    ///         are consuming had at least one raise (that is what fired the
    ///         init signal), and the ctor should snapshot the sink via
    ///         <c>Sense()</c> to catch up on anything that happened between
    ///         the raise and the ctor completing.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddOnInitSignal<TCoordinator>(
        this IServiceCollection services,
        string initSignal)
        where TCoordinator : class
    {
        services.AddInitSignalBus();
        services.TryAddSingleton<TCoordinator>();
        services.AddHostedService(sp =>
            new InitSignalBootstrap<TCoordinator>(
                sp.GetRequiredService<IInitSignalBus>(),
                initSignal,
                sp));
        return services;
    }
}

/// <summary>
///     Boot-time observer that subscribes to a single init signal and
///     resolves <typeparamref name="TCoordinator"/> from DI on the first
///     raise. One instance per registered coordinator; kept internal so
///     callers cannot depend on the shape.
/// </summary>
internal sealed class InitSignalBootstrap<TCoordinator> : IHostedService, IDisposable
    where TCoordinator : class
{
    private readonly IInitSignalBus _bus;
    private readonly string _initSignal;
    private readonly IServiceProvider _serviceProvider;
    private IDisposable? _subscription;

    public InitSignalBootstrap(
        IInitSignalBus bus,
        string initSignal,
        IServiceProvider serviceProvider)
    {
        _bus = bus;
        _initSignal = initSignal;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = _bus.Subscribe(_initSignal, ResolveCoordinator);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }

    private void ResolveCoordinator()
    {
        // Materialise the coordinator singleton. DI will construct it if it
        // has not been constructed yet; this is where subscription-in-ctor
        // shapes actually wire up their sink observers.
        _ = _serviceProvider.GetRequiredService<TCoordinator>();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}