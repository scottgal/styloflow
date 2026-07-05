using System.Collections.Concurrent;

namespace StyloFlow.Orchestration;

/// <summary>
///     Default in-process implementation of <see cref="IInitSignalBus"/>.
///     Thread-safe. Kept small on purpose -- the primitive's whole job is
///     "fire this action the first time this name is raised, or immediately
///     if the raise already happened."
/// </summary>
public sealed class InitSignalBus : IInitSignalBus
{
    private readonly ConcurrentDictionary<string, SignalState> _states = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool Raise(string initSignal)
    {
        var state = _states.GetOrAdd(initSignal, _ => new SignalState());
        List<Action>? toFire = null;

        lock (state.Lock)
        {
            if (state.Fired) return false;
            state.Fired = true;
            // Snapshot handlers under lock so late unsubscribes cannot race
            // with a fire in progress. Handlers registered AFTER we release
            // the lock invoke immediately from Subscribe.
            if (state.Handlers.Count > 0)
                toFire = new List<Action>(state.Handlers);
        }

        if (toFire is null) return true;
        foreach (var handler in toFire)
        {
            try { handler(); }
            catch { /* deliberately swallowed -- see interface remarks */ }
        }
        return true;
    }

    /// <inheritdoc />
    public IDisposable Subscribe(string initSignal, Action handler)
    {
        var state = _states.GetOrAdd(initSignal, _ => new SignalState());
        var fireNow = false;

        lock (state.Lock)
        {
            if (state.Fired)
            {
                fireNow = true;
            }
            else
            {
                state.Handlers.Add(handler);
            }
        }

        if (fireNow)
        {
            try { handler(); }
            catch { /* deliberately swallowed */ }
        }

        return new Subscription(this, initSignal, handler);
    }

    /// <inheritdoc />
    public bool HasFired(string initSignal)
    {
        if (!_states.TryGetValue(initSignal, out var state)) return false;
        lock (state.Lock) return state.Fired;
    }

    private void Unsubscribe(string initSignal, Action handler)
    {
        if (!_states.TryGetValue(initSignal, out var state)) return;
        lock (state.Lock) state.Handlers.Remove(handler);
    }

    private sealed class SignalState
    {
        public readonly object Lock = new();
        public bool Fired;
        public readonly List<Action> Handlers = new();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly InitSignalBus _bus;
        private readonly string _initSignal;
        private readonly Action _handler;
        private int _disposed;

        public Subscription(InitSignalBus bus, string initSignal, Action handler)
        {
            _bus = bus;
            _initSignal = initSignal;
            _handler = handler;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _bus.Unsubscribe(_initSignal, _handler);
        }
    }
}