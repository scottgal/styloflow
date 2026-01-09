using System.Collections.Concurrent;
using System.Diagnostics;
using StyloFlow.Retrieval.Analysis;

namespace StyloFlow.Retrieval.Orchestration;

/// <summary>
/// Coordinates wave execution across content types.
/// Handles dependency resolution, concurrency lanes, and signal aggregation.
/// Works with documents, images, audio, video - any content type.
/// </summary>
public class WaveCoordinator
{
    private readonly Dictionary<string, IContentAnalysisWave> _waves = new();
    private readonly Dictionary<string, SemaphoreSlim> _lanes = new();
    private readonly WaveManifestLoader _manifestLoader;

    public WaveCoordinator(WaveManifestLoader? manifestLoader = null)
    {
        _manifestLoader = manifestLoader ?? new WaveManifestLoader();
    }

    /// <summary>
    /// Register a wave for coordination.
    /// </summary>
    public void RegisterWave(IContentAnalysisWave wave)
    {
        _waves[wave.Name] = wave;
    }

    /// <summary>
    /// Register multiple waves.
    /// </summary>
    public void RegisterWaves(IEnumerable<IContentAnalysisWave> waves)
    {
        foreach (var wave in waves)
            RegisterWave(wave);
    }

    /// <summary>
    /// Execute all registered waves for a content path.
    /// </summary>
    public async Task<CoordinatorResult> ExecuteAsync(
        string contentPath,
        AnalysisContext? context = null,
        CoordinatorProfile? profile = null,
        CancellationToken ct = default)
    {
        context ??= new AnalysisContext();
        profile ??= CoordinatorProfile.Default;

        var sw = Stopwatch.StartNew();
        var signals = new ConcurrentBag<Signal>();
        var executionLog = new ConcurrentBag<WaveExecutionLog>();

        // Get waves sorted by priority (highest first)
        var orderedWaves = _waves.Values
            .Where(w => w.Enabled)
            .OrderByDescending(w => w.Priority)
            .ToList();

        // Filter by profile's enabled waves if specified
        if (profile.EnabledWaves.Count > 0)
        {
            orderedWaves = orderedWaves
                .Where(w => profile.EnabledWaves.Contains(w.Name))
                .ToList();
        }

        // Execute waves respecting dependencies
        foreach (var wave in orderedWaves)
        {
            if (ct.IsCancellationRequested) break;

            // Check if wave should run
            if (!wave.ShouldRun(contentPath, context))
            {
                executionLog.Add(new WaveExecutionLog
                {
                    WaveName = wave.Name,
                    Status = "skipped",
                    Reason = "ShouldRun returned false"
                });
                continue;
            }

            // Check if skipped by routing
            if (context.IsWaveSkipped(wave.Name))
            {
                executionLog.Add(new WaveExecutionLog
                {
                    WaveName = wave.Name,
                    Status = "skipped",
                    Reason = "Skipped by routing"
                });
                continue;
            }

            // Get lane semaphore for concurrency control
            var lane = GetLaneForWave(wave, profile);
            var semaphore = GetLaneSemaphore(lane);

            try
            {
                await semaphore.WaitAsync(ct);
                var waveStart = Stopwatch.StartNew();

                try
                {
                    // Execute wave with timeout
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(profile.WaveTimeoutMs);

                    var waveSignals = await wave.AnalyzeAsync(contentPath, context, cts.Token);
                    var signalList = waveSignals.ToList();

                    // Add signals to context and collection
                    context.AddSignals(signalList);
                    foreach (var signal in signalList)
                        signals.Add(signal);

                    executionLog.Add(new WaveExecutionLog
                    {
                        WaveName = wave.Name,
                        Status = "success",
                        DurationMs = waveStart.ElapsedMilliseconds,
                        SignalCount = signalList.Count
                    });
                }
                catch (OperationCanceledException)
                {
                    executionLog.Add(new WaveExecutionLog
                    {
                        WaveName = wave.Name,
                        Status = "timeout",
                        DurationMs = waveStart.ElapsedMilliseconds
                    });
                }
                catch (Exception ex)
                {
                    executionLog.Add(new WaveExecutionLog
                    {
                        WaveName = wave.Name,
                        Status = "error",
                        Error = ex.Message,
                        DurationMs = waveStart.ElapsedMilliseconds
                    });

                    // Add error signal
                    signals.Add(new Signal
                    {
                        Key = $"{wave.Name.ToLowerInvariant()}.error",
                        Value = ex.Message,
                        Confidence = 1.0,
                        Source = wave.Name,
                        Tags = new List<string> { "error" }
                    });
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        sw.Stop();

        return new CoordinatorResult
        {
            Signals = signals.ToList(),
            ExecutionLog = executionLog.ToList(),
            TotalDurationMs = sw.ElapsedMilliseconds,
            Profile = profile.Name
        };
    }

    private LaneConfig GetLaneForWave(IContentAnalysisWave wave, CoordinatorProfile profile)
    {
        // Check if wave has specific lane in manifest
        var manifest = _manifestLoader.GetManifest(wave.Name);
        if (manifest?.Lane != null)
            return manifest.Lane;

        // Default lane based on tags
        if (wave.Tags.Contains("ml") || wave.Tags.Contains("llm"))
            return profile.Lanes.GetValueOrDefault("ml", new LaneConfig { Name = "ml", MaxConcurrency = 1 });

        if (wave.Tags.Contains("io"))
            return profile.Lanes.GetValueOrDefault("io", new LaneConfig { Name = "io", MaxConcurrency = 4 });

        return profile.Lanes.GetValueOrDefault("fast", new LaneConfig { Name = "fast", MaxConcurrency = 8 });
    }

    private SemaphoreSlim GetLaneSemaphore(LaneConfig lane)
    {
        if (!_lanes.TryGetValue(lane.Name, out var semaphore))
        {
            semaphore = new SemaphoreSlim(lane.MaxConcurrency, lane.MaxConcurrency);
            _lanes[lane.Name] = semaphore;
        }
        return semaphore;
    }
}

/// <summary>
/// Result from coordinator execution.
/// </summary>
public record CoordinatorResult
{
    public IReadOnlyList<Signal> Signals { get; init; } = Array.Empty<Signal>();
    public IReadOnlyList<WaveExecutionLog> ExecutionLog { get; init; } = Array.Empty<WaveExecutionLog>();
    public long TotalDurationMs { get; init; }
    public string Profile { get; init; } = "default";
}

/// <summary>
/// Execution log entry for a single wave.
/// </summary>
public record WaveExecutionLog
{
    public required string WaveName { get; init; }
    public required string Status { get; init; }
    public long DurationMs { get; init; }
    public int SignalCount { get; init; }
    public string? Error { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Profile for coordinator execution settings.
/// </summary>
public class CoordinatorProfile
{
    public string Name { get; init; } = "default";
    public string Description { get; init; } = "";
    public int WaveTimeoutMs { get; init; } = 30000;
    public Dictionary<string, LaneConfig> Lanes { get; init; } = new();
    public HashSet<string> EnabledWaves { get; init; } = new();
    public HashSet<string> DisabledWaves { get; init; } = new();

    public static CoordinatorProfile Default => new()
    {
        Name = "default",
        Description = "Default balanced profile",
        WaveTimeoutMs = 30000,
        Lanes = new Dictionary<string, LaneConfig>
        {
            ["fast"] = new() { Name = "fast", MaxConcurrency = 8 },
            ["io"] = new() { Name = "io", MaxConcurrency = 4 },
            ["ml"] = new() { Name = "ml", MaxConcurrency = 2 },
            ["llm"] = new() { Name = "llm", MaxConcurrency = 1 }
        }
    };

    public static CoordinatorProfile Fast => new()
    {
        Name = "fast",
        Description = "Fast profile - skip expensive operations",
        WaveTimeoutMs = 5000,
        Lanes = new Dictionary<string, LaneConfig>
        {
            ["fast"] = new() { Name = "fast", MaxConcurrency = 16 },
            ["io"] = new() { Name = "io", MaxConcurrency = 8 }
        }
    };

    public static CoordinatorProfile Quality => new()
    {
        Name = "quality",
        Description = "Quality profile - run all waves including LLM",
        WaveTimeoutMs = 60000,
        Lanes = new Dictionary<string, LaneConfig>
        {
            ["fast"] = new() { Name = "fast", MaxConcurrency = 4 },
            ["io"] = new() { Name = "io", MaxConcurrency = 2 },
            ["ml"] = new() { Name = "ml", MaxConcurrency = 2 },
            ["llm"] = new() { Name = "llm", MaxConcurrency = 1 }
        }
    };
}
