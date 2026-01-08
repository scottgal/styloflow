using System.Collections.Concurrent;
using Mostlylucid.Ephemeral;

namespace StyloFlow.WorkflowBuilder.Runtime;

/// <summary>
/// Wrapper around Ephemeral's SignalSink providing workflow-specific helpers.
/// Uses the real SignalSink from Ephemeral - not a custom reimplementation.
/// Includes sliding window support for entity processing with sampling.
/// </summary>
public sealed class WorkflowSignals : IDisposable
{
    private readonly SignalSink _sink;
    private readonly TypedSignalSink<WorkflowPayload> _typedSink;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly string _runId;
    private long _operationId;

    // Sliding window tracking for entity processing
    private readonly ConcurrentDictionary<string, SignalWindow> _windows = new();
    private readonly HashSet<string> _processedKeys = [];
    private readonly object _processedLock = new();

    public SignalSink Sink => _sink;
    public TypedSignalSink<WorkflowPayload> TypedSink => _typedSink;
    public string RunId => _runId;

    public WorkflowSignals(string runId, SignalSink? sharedSink = null)
    {
        _runId = runId;
        _sink = sharedSink ?? new SignalSink(maxCapacity: 1000, maxAge: TimeSpan.FromMinutes(5));
        _typedSink = new TypedSignalSink<WorkflowPayload>(_sink);
        _operationId = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// Emit a signal with a value. Uses Ephemeral's SignalSink under the hood.
    /// </summary>
    public void Emit(string signal, object? value, string sourceNode, double confidence = 1.0)
    {
        // Create typed payload
        var payload = new WorkflowPayload(signal, value, sourceNode, confidence, _runId);

        // Emit to typed sink (which also raises on untyped sink)
        _typedSink.Raise(signal, payload, key: $"{_runId}:{sourceNode}");
    }

    /// <summary>
    /// Get a signal value by pattern matching recent signals.
    /// </summary>
    public T? Get<T>(string signalPattern)
    {
        var signals = _sink.Sense(e => e.Is(signalPattern) || e.StartsWith(signalPattern));
        if (signals.Count == 0) return default;

        // Get most recent matching signal
        var latest = signals.OrderByDescending(s => s.Timestamp).FirstOrDefault();

        // For typed signals, we need to look in the typed sink
        var typedSignals = _typedSink.Sense(e => e.Signal == signalPattern);
        if (typedSignals.Count > 0)
        {
            var typedLatest = typedSignals.OrderByDescending(s => s.Timestamp).First();
            if (typedLatest.Payload.Value is T typed)
                return typed;
        }

        return default;
    }

    /// <summary>
    /// Check if a signal exists in the window.
    /// </summary>
    public bool Has(string signalPattern) => _sink.Detect(signalPattern);

    /// <summary>
    /// Subscribe to all signals for broadcasting (e.g., to SignalR).
    /// </summary>
    public IDisposable Subscribe(Action<SignalEvent> handler)
    {
        var sub = _sink.Subscribe(handler);
        _subscriptions.Add(sub);
        return sub;
    }

    /// <summary>
    /// Subscribe to typed workflow signals.
    /// </summary>
    public void SubscribeTyped(Action<SignalEvent<WorkflowPayload>> handler)
    {
        _typedSink.TypedSignalRaised += handler;
    }

    /// <summary>
    /// Get all signals in the current window.
    /// </summary>
    public IReadOnlyList<SignalEvent> GetAll() => _sink.Sense();

    /// <summary>
    /// Get signals for a specific operation/run.
    /// </summary>
    public IReadOnlyList<SignalEvent> GetRunSignals()
    {
        return _sink.Sense(e => e.Key?.StartsWith(_runId) == true);
    }

    #region Sliding Window Support

    /// <summary>
    /// Get or create a named sliding window for entity processing.
    /// Windows allow sampling, behavioral analysis, and cache warming.
    /// </summary>
    public SignalWindow GetWindow(string windowName, int maxItems = 100, TimeSpan? maxAge = null)
    {
        return _windows.GetOrAdd(windowName, _ => new SignalWindow(
            windowName,
            maxItems,
            maxAge ?? TimeSpan.FromMinutes(10)));
    }

    /// <summary>
    /// Add an entity to a named window for tracking.
    /// </summary>
    public void WindowAdd<T>(string windowName, string key, T entity) where T : class
    {
        var window = GetWindow(windowName);
        window.Add(key, entity);
    }

    /// <summary>
    /// Query entities from a sliding window with optional time range.
    /// </summary>
    public IReadOnlyList<WindowEntry> WindowQuery(string windowName, TimeSpan? since = null, int? limit = null)
    {
        if (!_windows.TryGetValue(windowName, out var window))
            return [];

        return window.Query(since, limit);
    }

    /// <summary>
    /// Sample N random entities from a window (useful for behavioral analysis).
    /// </summary>
    public IReadOnlyList<WindowEntry> WindowSample(string windowName, int count)
    {
        if (!_windows.TryGetValue(windowName, out var window))
            return [];

        return window.Sample(count);
    }

    /// <summary>
    /// Get aggregated stats for a window (count, avg values, etc.).
    /// </summary>
    public WindowStats WindowStats(string windowName)
    {
        if (!_windows.TryGetValue(windowName, out var window))
            return new WindowStats(windowName, 0, null, null, TimeSpan.Zero);

        return window.GetStats();
    }

    /// <summary>
    /// Mark an entity key as processed (for deduplication).
    /// </summary>
    public void MarkProcessed(string key)
    {
        lock (_processedLock)
        {
            _processedKeys.Add(key);
        }
    }

    /// <summary>
    /// Check if an entity key has been processed.
    /// </summary>
    public bool IsProcessed(string key)
    {
        lock (_processedLock)
        {
            return _processedKeys.Contains(key);
        }
    }

    /// <summary>
    /// Get unprocessed entities from a window.
    /// </summary>
    public IReadOnlyList<WindowEntry> GetUnprocessed(string windowName)
    {
        if (!_windows.TryGetValue(windowName, out var window))
            return [];

        lock (_processedLock)
        {
            return window.Query()
                .Where(e => !_processedKeys.Contains(e.Key))
                .ToList();
        }
    }

    /// <summary>
    /// Find behavioral patterns in windowed data (e.g., burst detection).
    /// </summary>
    public IReadOnlyList<BehavioralPattern> DetectPatterns(string windowName, PatternType patternType)
    {
        if (!_windows.TryGetValue(windowName, out var window))
            return [];

        return window.DetectPatterns(patternType);
    }

    #endregion

    public void Dispose()
    {
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();
        _windows.Clear();
    }
}

/// <summary>
/// Typed payload for workflow signals, carrying metadata alongside the value.
/// </summary>
public sealed record WorkflowPayload(
    string Signal,
    object? Value,
    string SourceNode,
    double Confidence,
    string RunId)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Sliding window for entity processing with sampling, deduplication, and pattern detection.
/// Inspired by BotDetection's HTTP request window for behavioral analysis.
/// </summary>
public sealed class SignalWindow
{
    private readonly string _name;
    private readonly int _maxItems;
    private readonly TimeSpan _maxAge;
    private readonly LinkedList<WindowEntry> _entries = new();
    private readonly Dictionary<string, LinkedListNode<WindowEntry>> _keyIndex = new();
    private readonly object _lock = new();
    private static readonly Random _random = new();

    public string Name => _name;
    public int MaxItems => _maxItems;
    public TimeSpan MaxAge => _maxAge;

    public SignalWindow(string name, int maxItems, TimeSpan maxAge)
    {
        _name = name;
        _maxItems = maxItems;
        _maxAge = maxAge;
    }

    /// <summary>
    /// Add an entity to the window. Older entries are evicted based on count/age.
    /// </summary>
    public void Add<T>(string key, T entity) where T : class
    {
        lock (_lock)
        {
            var entry = new WindowEntry(key, entity, DateTimeOffset.UtcNow);

            // Remove existing entry with same key (update semantics)
            if (_keyIndex.TryGetValue(key, out var existing))
            {
                _entries.Remove(existing);
                _keyIndex.Remove(key);
            }

            // Add to end (most recent)
            var node = _entries.AddLast(entry);
            _keyIndex[key] = node;

            // Evict old entries
            EvictExpired();
            EvictOverflow();
        }
    }

    /// <summary>
    /// Query entries with optional time filter and limit.
    /// </summary>
    public IReadOnlyList<WindowEntry> Query(TimeSpan? since = null, int? limit = null)
    {
        lock (_lock)
        {
            EvictExpired();

            IEnumerable<WindowEntry> result = _entries;

            if (since.HasValue)
            {
                var cutoff = DateTimeOffset.UtcNow - since.Value;
                result = result.Where(e => e.Timestamp >= cutoff);
            }

            if (limit.HasValue)
            {
                result = result.TakeLast(limit.Value);
            }

            return result.ToList();
        }
    }

    /// <summary>
    /// Random sample N entries from the window (for behavioral analysis).
    /// </summary>
    public IReadOnlyList<WindowEntry> Sample(int count)
    {
        lock (_lock)
        {
            EvictExpired();

            if (_entries.Count <= count)
                return _entries.ToList();

            // Reservoir sampling for fair random selection
            var result = new List<WindowEntry>(count);
            var i = 0;
            foreach (var entry in _entries)
            {
                if (result.Count < count)
                {
                    result.Add(entry);
                }
                else
                {
                    var j = _random.Next(i + 1);
                    if (j < count)
                    {
                        result[j] = entry;
                    }
                }
                i++;
            }
            return result;
        }
    }

    /// <summary>
    /// Get entry by key.
    /// </summary>
    public WindowEntry? Get(string key)
    {
        lock (_lock)
        {
            return _keyIndex.TryGetValue(key, out var node) ? node.Value : null;
        }
    }

    /// <summary>
    /// Check if key exists in window.
    /// </summary>
    public bool Contains(string key)
    {
        lock (_lock)
        {
            return _keyIndex.ContainsKey(key);
        }
    }

    /// <summary>
    /// Get aggregated statistics for the window.
    /// </summary>
    public WindowStats GetStats()
    {
        lock (_lock)
        {
            EvictExpired();

            if (_entries.Count == 0)
                return new WindowStats(_name, 0, null, null, TimeSpan.Zero);

            var oldest = _entries.First?.Value.Timestamp;
            var newest = _entries.Last?.Value.Timestamp;
            var span = newest.HasValue && oldest.HasValue
                ? newest.Value - oldest.Value
                : TimeSpan.Zero;

            return new WindowStats(_name, _entries.Count, oldest, newest, span);
        }
    }

    /// <summary>
    /// Detect behavioral patterns in the window.
    /// </summary>
    public IReadOnlyList<BehavioralPattern> DetectPatterns(PatternType patternType)
    {
        lock (_lock)
        {
            EvictExpired();

            var patterns = new List<BehavioralPattern>();
            var entries = _entries.ToList();

            switch (patternType)
            {
                case PatternType.Burst:
                    patterns.AddRange(DetectBurstPatterns(entries));
                    break;
                case PatternType.Periodic:
                    patterns.AddRange(DetectPeriodicPatterns(entries));
                    break;
                case PatternType.Anomaly:
                    patterns.AddRange(DetectAnomalyPatterns(entries));
                    break;
                case PatternType.All:
                    patterns.AddRange(DetectBurstPatterns(entries));
                    patterns.AddRange(DetectPeriodicPatterns(entries));
                    patterns.AddRange(DetectAnomalyPatterns(entries));
                    break;
            }

            return patterns;
        }
    }

    private IEnumerable<BehavioralPattern> DetectBurstPatterns(List<WindowEntry> entries)
    {
        if (entries.Count < 3) yield break;

        // Detect rapid succession of entries (burst)
        const int burstThreshold = 5;
        var burstWindow = TimeSpan.FromSeconds(10);

        for (int i = 0; i <= entries.Count - burstThreshold; i++)
        {
            var windowEnd = entries[i].Timestamp + burstWindow;
            var count = entries.Skip(i).TakeWhile(e => e.Timestamp <= windowEnd).Count();

            if (count >= burstThreshold)
            {
                yield return new BehavioralPattern(
                    PatternType.Burst,
                    $"Burst detected: {count} entries in {burstWindow.TotalSeconds}s",
                    entries[i].Timestamp,
                    (double)count / burstThreshold);
            }
        }
    }

    private IEnumerable<BehavioralPattern> DetectPeriodicPatterns(List<WindowEntry> entries)
    {
        if (entries.Count < 5) yield break;

        // Calculate intervals between consecutive entries
        var intervals = new List<double>();
        for (int i = 1; i < entries.Count; i++)
        {
            intervals.Add((entries[i].Timestamp - entries[i - 1].Timestamp).TotalSeconds);
        }

        if (intervals.Count < 3) yield break;

        // Check for regularity (low standard deviation relative to mean)
        var mean = intervals.Average();
        var stdDev = Math.Sqrt(intervals.Average(v => Math.Pow(v - mean, 2)));
        var cv = mean > 0 ? stdDev / mean : double.MaxValue;

        if (cv < 0.3) // Coefficient of variation < 30% suggests periodicity
        {
            yield return new BehavioralPattern(
                PatternType.Periodic,
                $"Periodic pattern: ~{mean:F1}s intervals (CV={cv:F2})",
                entries.First().Timestamp,
                1.0 - cv);
        }
    }

    private IEnumerable<BehavioralPattern> DetectAnomalyPatterns(List<WindowEntry> entries)
    {
        if (entries.Count < 10) yield break;

        // Use IQR method for outlier detection on intervals
        var intervals = new List<double>();
        for (int i = 1; i < entries.Count; i++)
        {
            intervals.Add((entries[i].Timestamp - entries[i - 1].Timestamp).TotalSeconds);
        }

        if (intervals.Count < 5) yield break;

        var sorted = intervals.OrderBy(x => x).ToList();
        var q1 = sorted[sorted.Count / 4];
        var q3 = sorted[sorted.Count * 3 / 4];
        var iqr = q3 - q1;
        var lowerBound = q1 - 1.5 * iqr;
        var upperBound = q3 + 1.5 * iqr;

        for (int i = 0; i < intervals.Count; i++)
        {
            if (intervals[i] < lowerBound || intervals[i] > upperBound)
            {
                yield return new BehavioralPattern(
                    PatternType.Anomaly,
                    $"Anomalous interval: {intervals[i]:F1}s (expected {q1:F1}-{q3:F1}s)",
                    entries[i + 1].Timestamp,
                    Math.Abs(intervals[i] - (q1 + q3) / 2) / iqr);
            }
        }
    }

    private void EvictExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - _maxAge;
        while (_entries.First != null && _entries.First.Value.Timestamp < cutoff)
        {
            var entry = _entries.First.Value;
            _keyIndex.Remove(entry.Key);
            _entries.RemoveFirst();
        }
    }

    private void EvictOverflow()
    {
        while (_entries.Count > _maxItems && _entries.First != null)
        {
            var entry = _entries.First.Value;
            _keyIndex.Remove(entry.Key);
            _entries.RemoveFirst();
        }
    }
}

/// <summary>
/// Entry in a signal window.
/// </summary>
public sealed record WindowEntry(string Key, object Entity, DateTimeOffset Timestamp);

/// <summary>
/// Aggregated statistics for a signal window.
/// </summary>
public sealed record WindowStats(
    string WindowName,
    int Count,
    DateTimeOffset? OldestEntry,
    DateTimeOffset? NewestEntry,
    TimeSpan TimeSpan);

/// <summary>
/// Detected behavioral pattern from windowed data.
/// </summary>
public sealed record BehavioralPattern(
    PatternType Type,
    string Description,
    DateTimeOffset DetectedAt,
    double Confidence);

/// <summary>
/// Types of behavioral patterns to detect.
/// </summary>
public enum PatternType
{
    Burst,      // Rapid succession of events
    Periodic,   // Regular intervals between events
    Anomaly,    // Unusual timing patterns
    All         // Detect all pattern types
}
