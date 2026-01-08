using System.Collections.Concurrent;
using StyloFlow.Dashboard.Configuration;
using StyloFlow.Dashboard.Models;

namespace StyloFlow.Dashboard.Services;

/// <summary>
/// Thread-safe in-memory event store with circular buffer behavior.
/// </summary>
public class InMemoryDashboardEventStore : IDashboardEventStore
{
    private readonly ConcurrentQueue<DashboardEvent> _events = new();
    private readonly int _maxEvents;
    private int _totalEventsReceived;
    private DateTime _firstEventTime = DateTime.UtcNow;

    public InMemoryDashboardEventStore(DashboardOptions options)
    {
        _maxEvents = options.MaxEventsInMemory;
    }

    public Task AddEventAsync(DashboardEvent evt)
    {
        _events.Enqueue(evt);
        Interlocked.Increment(ref _totalEventsReceived);

        // Trim if over capacity
        while (_events.Count > _maxEvents && _events.TryDequeue(out _)) { }

        return Task.CompletedTask;
    }

    public Task AddEventsAsync(IEnumerable<DashboardEvent> events)
    {
        foreach (var evt in events)
        {
            _events.Enqueue(evt);
            Interlocked.Increment(ref _totalEventsReceived);
        }

        while (_events.Count > _maxEvents && _events.TryDequeue(out _)) { }

        return Task.CompletedTask;
    }

    public Task<List<DashboardEvent>> GetEventsAsync(DashboardFilter? filter = null)
    {
        var query = _events.AsEnumerable();

        if (filter != null)
        {
            if (filter.StartTime.HasValue)
                query = query.Where(e => e.Timestamp >= filter.StartTime.Value);

            if (filter.EndTime.HasValue)
                query = query.Where(e => e.Timestamp <= filter.EndTime.Value);

            if (filter.EventTypes?.Count > 0)
                query = query.Where(e => filter.EventTypes.Contains(e.EventType));

            if (filter.Severities?.Count > 0)
                query = query.Where(e => filter.Severities.Contains(e.Severity));

            if (filter.Sources?.Count > 0)
                query = query.Where(e => e.Source != null && filter.Sources.Contains(e.Source));

            if (filter.Tags?.Count > 0)
                query = query.Where(e => e.Tags.Any(t => filter.Tags.Contains(t)));

            if (!string.IsNullOrEmpty(filter.MessageContains))
                query = query.Where(e => e.Message?.Contains(filter.MessageContains, StringComparison.OrdinalIgnoreCase) == true);

            query = query.Skip(filter.Offset).Take(filter.Limit);
        }

        return Task.FromResult(query.OrderByDescending(e => e.Timestamp).ToList());
    }

    public Task<DashboardSummary> GetSummaryAsync(int timeWindowSeconds = 300)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-timeWindowSeconds);
        var recentEvents = _events.Where(e => e.Timestamp >= cutoff).ToList();

        var elapsedSeconds = Math.Max(1, (DateTime.UtcNow - _firstEventTime).TotalSeconds);

        var summary = new DashboardSummary
        {
            Timestamp = DateTime.UtcNow,
            TotalEvents = recentEvents.Count,
            EventsByType = recentEvents
                .GroupBy(e => e.EventType)
                .ToDictionary(g => g.Key, g => g.Count()),
            EventsBySeverity = recentEvents
                .GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key, g => g.Count()),
            EventsBySource = recentEvents
                .Where(e => e.Source != null)
                .GroupBy(e => e.Source!)
                .ToDictionary(g => g.Key, g => g.Count()),
            AverageProcessingTimeMs = recentEvents
                .Where(e => e.ProcessingTimeMs.HasValue)
                .Select(e => e.ProcessingTimeMs!.Value)
                .DefaultIfEmpty(0)
                .Average(),
            EventsPerSecond = recentEvents.Count / (double)timeWindowSeconds,
            TimeWindowSeconds = timeWindowSeconds
        };

        return Task.FromResult(summary);
    }

    public Task<List<TimeSeriesPoint>> GetTimeSeriesAsync(
        DateTime startTime,
        DateTime endTime,
        TimeSpan bucketSize)
    {
        var buckets = new List<TimeSeriesPoint>();
        var currentBucket = startTime;

        while (currentBucket < endTime)
        {
            var nextBucket = currentBucket.Add(bucketSize);
            var bucketEvents = _events
                .Where(e => e.Timestamp >= currentBucket && e.Timestamp < nextBucket)
                .ToList();

            var point = new TimeSeriesPoint
            {
                Timestamp = currentBucket,
                TotalCount = bucketEvents.Count,
                CountsByType = bucketEvents
                    .GroupBy(e => e.EventType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountsBySeverity = bucketEvents
                    .GroupBy(e => e.Severity)
                    .ToDictionary(g => g.Key, g => g.Count()),
                AverageProcessingTimeMs = bucketEvents
                    .Where(e => e.ProcessingTimeMs.HasValue)
                    .Select(e => e.ProcessingTimeMs!.Value)
                    .DefaultIfEmpty(0)
                    .Average()
            };

            buckets.Add(point);
            currentBucket = nextBucket;
        }

        return Task.FromResult(buckets);
    }

    public Task<int> GetCountAsync(DashboardFilter? filter = null)
    {
        var query = _events.AsEnumerable();

        if (filter != null)
        {
            if (filter.StartTime.HasValue)
                query = query.Where(e => e.Timestamp >= filter.StartTime.Value);

            if (filter.EndTime.HasValue)
                query = query.Where(e => e.Timestamp <= filter.EndTime.Value);

            if (filter.EventTypes?.Count > 0)
                query = query.Where(e => filter.EventTypes.Contains(e.EventType));

            if (filter.Severities?.Count > 0)
                query = query.Where(e => filter.Severities.Contains(e.Severity));
        }

        return Task.FromResult(query.Count());
    }

    public Task<List<string>> GetDistinctValuesAsync(string field, int limit = 100)
    {
        IEnumerable<string?> values = field.ToLowerInvariant() switch
        {
            "eventtype" or "type" => _events.Select(e => e.EventType),
            "severity" => _events.Select(e => e.Severity),
            "source" => _events.Select(e => e.Source),
            _ => Enumerable.Empty<string?>()
        };

        return Task.FromResult(values
            .Where(v => v != null)
            .Cast<string>()
            .Distinct()
            .Take(limit)
            .ToList());
    }

    public Task ClearAsync()
    {
        while (_events.TryDequeue(out _)) { }
        _totalEventsReceived = 0;
        _firstEventTime = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
