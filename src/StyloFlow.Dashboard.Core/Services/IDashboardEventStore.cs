using StyloFlow.Dashboard.Models;

namespace StyloFlow.Dashboard.Services;

/// <summary>
/// Interface for storing and retrieving dashboard events.
/// Implement this to provide custom storage (e.g., database, Redis).
/// </summary>
public interface IDashboardEventStore
{
    /// <summary>
    /// Add a new event to the store.
    /// </summary>
    Task AddEventAsync(DashboardEvent evt);

    /// <summary>
    /// Add multiple events in batch.
    /// </summary>
    Task AddEventsAsync(IEnumerable<DashboardEvent> events);

    /// <summary>
    /// Get events matching the specified filter.
    /// </summary>
    Task<List<DashboardEvent>> GetEventsAsync(DashboardFilter? filter = null);

    /// <summary>
    /// Get a summary of events in the store.
    /// </summary>
    Task<DashboardSummary> GetSummaryAsync(int timeWindowSeconds = 300);

    /// <summary>
    /// Get time series data for charts.
    /// </summary>
    Task<List<TimeSeriesPoint>> GetTimeSeriesAsync(
        DateTime startTime,
        DateTime endTime,
        TimeSpan bucketSize);

    /// <summary>
    /// Get the count of events matching the filter.
    /// </summary>
    Task<int> GetCountAsync(DashboardFilter? filter = null);

    /// <summary>
    /// Get distinct values for a metadata field (for filter dropdowns).
    /// </summary>
    Task<List<string>> GetDistinctValuesAsync(string field, int limit = 100);

    /// <summary>
    /// Clear all events from the store.
    /// </summary>
    Task ClearAsync();
}
