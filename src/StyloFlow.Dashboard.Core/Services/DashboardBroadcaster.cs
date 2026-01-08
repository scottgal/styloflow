using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StyloFlow.Dashboard.Configuration;
using StyloFlow.Dashboard.Hubs;

namespace StyloFlow.Dashboard.Services;

/// <summary>
/// Background service that periodically broadcasts summary statistics.
/// </summary>
public class DashboardBroadcaster : BackgroundService
{
    private readonly IHubContext<DashboardHub, IDashboardHub> _hubContext;
    private readonly IDashboardEventStore _eventStore;
    private readonly DashboardOptions _options;
    private readonly ILogger<DashboardBroadcaster> _logger;

    public DashboardBroadcaster(
        IHubContext<DashboardHub, IDashboardHub> hubContext,
        IDashboardEventStore eventStore,
        DashboardOptions options,
        ILogger<DashboardBroadcaster> logger)
    {
        _hubContext = hubContext;
        _eventStore = eventStore;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.SummaryBroadcastIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var summary = await _eventStore.GetSummaryAsync();
                await _hubContext.Clients.All.BroadcastSummary(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting dashboard summary");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
