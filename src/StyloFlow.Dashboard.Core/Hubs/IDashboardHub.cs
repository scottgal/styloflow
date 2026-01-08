using StyloFlow.Dashboard.Models;

namespace StyloFlow.Dashboard.Hubs;

/// <summary>
/// SignalR hub contract for dashboard real-time updates.
/// </summary>
public interface IDashboardHub
{
    /// <summary>
    /// Broadcast a new event to all connected clients.
    /// </summary>
    Task BroadcastEvent(DashboardEvent evt);

    /// <summary>
    /// Broadcast multiple events to all connected clients.
    /// </summary>
    Task BroadcastEvents(IEnumerable<DashboardEvent> events);

    /// <summary>
    /// Broadcast summary statistics to all connected clients.
    /// </summary>
    Task BroadcastSummary(DashboardSummary summary);

    /// <summary>
    /// Broadcast updated time series data.
    /// </summary>
    Task BroadcastTimeSeries(IEnumerable<TimeSeriesPoint> points);

    /// <summary>
    /// Broadcast a notification message.
    /// </summary>
    Task BroadcastNotification(string message, string severity = "Info");
}
