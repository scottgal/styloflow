namespace StyloFlow.Orchestration;

/// <summary>
///     Broker for boot-time <c>init.*</c> signals. Coordinators register a
///     factory against an init-signal name; the factory runs the first time
///     the signal is raised (which is typically the first time a producer
///     writes to the sink the coordinator would consume). Keeps the
///     coordinator entirely dormant -- no allocation, no subscription, no
///     background work -- until a real producer arrives.
/// </summary>
/// <remarks>
///     <para>
///         <b>Once-per-init semantics.</b> Each init signal fires at most
///         once per host lifetime. Repeated <see cref="Raise"/> calls after
///         the first no-op. Handlers registered after a signal has already
///         fired invoke immediately on <see cref="Subscribe"/> (mirrors
///         "you missed the boot, but you still need to run").
///     </para>
///     <para>
///         <b>Handlers execute inline on the raise thread.</b> Factories
///         should be cheap (they typically just construct a coordinator via
///         DI). Long-running work belongs inside the coordinator itself,
///         driven by whatever cadence signal the coordinator subscribes to
///         after construction.
///     </para>
///     <para>
///         <b>Handler exceptions are swallowed + logged upstream.</b> An
///         init-signal firing must never crash a caller in the producer's
///         hot path. The bus wraps every handler in try/catch; consumers
///         that need to know about init failures should observe the
///         coordinator itself for readiness.
///     </para>
/// </remarks>
public interface IInitSignalBus
{
    /// <summary>
    ///     Fire the init signal. Idempotent -- subsequent raises for the
    ///     same signal are no-ops.
    /// </summary>
    /// <param name="initSignal">The signal name (typically <c>init.foo</c>).</param>
    /// <returns>
    ///     <c>true</c> if this call actually fired the signal (first raise);
    ///     <c>false</c> if the signal had already been raised.
    /// </returns>
    bool Raise(string initSignal);

    /// <summary>
    ///     Register a handler for the given init signal. If the signal has
    ///     already fired, the handler runs synchronously before this method
    ///     returns.
    /// </summary>
    /// <returns>Handle whose disposal removes the handler.</returns>
    IDisposable Subscribe(string initSignal, Action handler);

    /// <summary>
    ///     True if the signal has been raised at least once.
    ///     Useful for tests + late-registered observers that want to
    ///     check without triggering the fire-immediately branch of
    ///     <see cref="Subscribe"/>.
    /// </summary>
    bool HasFired(string initSignal);
}