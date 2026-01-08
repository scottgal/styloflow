using Microsoft.AspNetCore.Http;

namespace StyloFlow.Dashboard.Configuration;

/// <summary>
/// Base configuration options for StyloFlow dashboards.
/// </summary>
public class DashboardOptions
{
    /// <summary>
    /// Base path for the dashboard (default: /styloflow).
    /// </summary>
    public string BasePath { get; set; } = "/styloflow";

    /// <summary>
    /// Path for the SignalR hub (default: /styloflow/hub).
    /// </summary>
    public string HubPath { get; set; } = "/styloflow/hub";

    /// <summary>
    /// Whether the dashboard is enabled (default: true).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Authorization policy name to require for dashboard access.
    /// </summary>
    public string? RequireAuthorizationPolicy { get; set; }

    /// <summary>
    /// Custom authorization filter function.
    /// </summary>
    public Func<HttpContext, Task<bool>>? AuthorizationFilter { get; set; }

    /// <summary>
    /// Maximum events to keep in memory (default: 1000).
    /// </summary>
    public int MaxEventsInMemory { get; set; } = 1000;

    /// <summary>
    /// Interval in seconds between summary broadcasts (default: 5).
    /// </summary>
    public int SummaryBroadcastIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Whether to enable the simulator for testing (default: false).
    /// </summary>
    public bool EnableSimulator { get; set; } = false;

    /// <summary>
    /// Events per second when simulator is enabled (default: 2).
    /// </summary>
    public int SimulatorEventsPerSecond { get; set; } = 2;

    /// <summary>
    /// Dashboard title displayed in the UI.
    /// </summary>
    public string Title { get; set; } = "StyloFlow Dashboard";

    /// <summary>
    /// Theme for the dashboard (light, dark, auto).
    /// </summary>
    public string Theme { get; set; } = "auto";
}
