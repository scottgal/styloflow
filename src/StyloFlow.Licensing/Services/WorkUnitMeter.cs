using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StyloFlow.Licensing.Models;

namespace StyloFlow.Licensing.Services;

/// <summary>
/// Tracks work unit consumption using a sliding window with time-based buckets.
/// Thread-safe implementation using lock-free data structures where possible.
/// </summary>
public sealed class WorkUnitMeter : IWorkUnitMeter, IDisposable
{
    private readonly StyloFlowOptions _options;
    private readonly ILicenseManager _licenseManager;
    private readonly ILogger<WorkUnitMeter> _logger;

    private readonly double[] _buckets;
    private readonly ConcurrentDictionary<string, double>[] _bucketsByType;
    private readonly TimeSpan _bucketDuration;
    private readonly object _lock = new();

    private int _currentBucketIndex;
    private DateTimeOffset _currentBucketStart;
    private readonly HashSet<int> _crossedThresholds = new();

    private readonly Timer _cleanupTimer;

    public WorkUnitMeter(
        StyloFlowOptions options,
        ILicenseManager licenseManager,
        ILogger<WorkUnitMeter> logger)
    {
        _options = options;
        _licenseManager = licenseManager;
        _logger = logger;

        var bucketCount = Math.Max(1, options.WorkUnitWindowBuckets);
        _buckets = new double[bucketCount];
        _bucketsByType = new ConcurrentDictionary<string, double>[bucketCount];
        for (int i = 0; i < bucketCount; i++)
        {
            _bucketsByType[i] = new ConcurrentDictionary<string, double>();
        }

        _bucketDuration = options.WorkUnitWindowSize / bucketCount;
        _currentBucketStart = DateTimeOffset.UtcNow;
        _currentBucketIndex = 0;

        // Cleanup timer to rotate buckets
        _cleanupTimer = new Timer(
            _ => RotateBuckets(),
            null,
            _bucketDuration,
            _bucketDuration);
    }

    public double CurrentWorkUnits
    {
        get
        {
            RotateBucketsIfNeeded();
            lock (_lock)
            {
                return _buckets.Sum();
            }
        }
    }

    public double MaxWorkUnits => _licenseManager.MaxWorkUnitsPerMinute;

    public double PercentUsed
    {
        get
        {
            var max = MaxWorkUnits;
            return max > 0 ? (CurrentWorkUnits / max) * 100 : 0;
        }
    }

    public bool IsThrottling => PercentUsed >= 100;

    public double ThrottleFactor
    {
        get
        {
            var percent = PercentUsed;
            if (percent < 80) return 1.0;
            if (percent >= 100) return 0.0;
            // Linear ramp down from 80% to 100%
            return 1.0 - ((percent - 80) / 20);
        }
    }

    public double HeadroomRemaining => Math.Max(0, MaxWorkUnits - CurrentWorkUnits);

    public event EventHandler<WorkUnitThresholdEvent>? ThresholdCrossed;

    public void Record(double workUnits, string? moleculeType = null)
    {
        if (workUnits <= 0) return;

        RotateBucketsIfNeeded();

        lock (_lock)
        {
            _buckets[_currentBucketIndex] += workUnits;

            if (!string.IsNullOrEmpty(moleculeType))
            {
                _bucketsByType[_currentBucketIndex].AddOrUpdate(
                    moleculeType,
                    workUnits,
                    (_, existing) => existing + workUnits);
            }
        }

        CheckThresholds();
    }

    public bool CanConsume(double workUnits)
    {
        return CurrentWorkUnits + workUnits <= MaxWorkUnits;
    }

    public WorkUnitSnapshot GetSnapshot()
    {
        RotateBucketsIfNeeded();

        lock (_lock)
        {
            var byType = new Dictionary<string, double>();
            foreach (var bucket in _bucketsByType)
            {
                foreach (var kvp in bucket)
                {
                    if (byType.TryGetValue(kvp.Key, out var existing))
                        byType[kvp.Key] = existing + kvp.Value;
                    else
                        byType[kvp.Key] = kvp.Value;
                }
            }

            return new WorkUnitSnapshot
            {
                CurrentWorkUnits = _buckets.Sum(),
                MaxWorkUnits = MaxWorkUnits,
                PercentUsed = PercentUsed,
                IsThrottling = IsThrottling,
                ThrottleFactor = ThrottleFactor,
                WindowStart = DateTimeOffset.UtcNow - _options.WorkUnitWindowSize,
                WindowEnd = DateTimeOffset.UtcNow,
                ByMoleculeType = byType
            };
        }
    }

    private void RotateBuckets()
    {
        lock (_lock)
        {
            RotateBucketsInternal();
        }
    }

    private void RotateBucketsIfNeeded()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _currentBucketStart < _bucketDuration)
            return;

        lock (_lock)
        {
            RotateBucketsInternal();
        }
    }

    private void RotateBucketsInternal()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _currentBucketStart;
        var bucketsToRotate = (int)(elapsed / _bucketDuration);

        if (bucketsToRotate <= 0) return;

        // Rotate multiple buckets if needed (e.g., if timer was delayed)
        for (int i = 0; i < Math.Min(bucketsToRotate, _buckets.Length); i++)
        {
            _currentBucketIndex = (_currentBucketIndex + 1) % _buckets.Length;
            _buckets[_currentBucketIndex] = 0;
            _bucketsByType[_currentBucketIndex].Clear();
        }

        _currentBucketStart = now;

        // Reset crossed thresholds when we rotate (allows re-triggering)
        if (PercentUsed < 80)
        {
            _crossedThresholds.Clear();
        }
    }

    private void CheckThresholds()
    {
        var percent = PercentUsed;

        foreach (var threshold in _options.WorkUnitThresholds)
        {
            if (percent >= threshold && !_crossedThresholds.Contains(threshold))
            {
                _crossedThresholds.Add(threshold);

                var evt = new WorkUnitThresholdEvent
                {
                    CurrentWorkUnits = CurrentWorkUnits,
                    MaxWorkUnits = MaxWorkUnits,
                    ThresholdPercent = threshold
                };

                _logger.LogWarning(
                    "Work unit threshold {Threshold}% crossed: {Current}/{Max} ({Percent:F1}%)",
                    threshold, evt.CurrentWorkUnits, evt.MaxWorkUnits, evt.PercentUsed);

                ThresholdCrossed?.Invoke(this, evt);
                _options.OnWorkUnitThreshold?.Invoke(evt);
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
