using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StyloFlow.Dashboard.Configuration;
using StyloFlow.Dashboard.Hubs;
using StyloFlow.Dashboard.Middleware;
using StyloFlow.Dashboard.Services;

namespace StyloFlow.Dashboard.Extensions;

/// <summary>
/// Extension methods for registering StyloFlow Dashboard services.
/// </summary>
public static class DashboardServiceExtensions
{
    /// <summary>
    /// Add StyloFlow Dashboard services to the service collection.
    /// </summary>
    public static IServiceCollection AddStyloFlowDashboard(
        this IServiceCollection services,
        Action<DashboardOptions>? configure = null)
    {
        var options = new DashboardOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSignalR();
        services.AddSingleton<IDashboardEventStore, InMemoryDashboardEventStore>();
        services.AddHostedService<DashboardBroadcaster>();

        return services;
    }

    /// <summary>
    /// Add StyloFlow Dashboard services with custom authorization filter.
    /// </summary>
    public static IServiceCollection AddStyloFlowDashboard(
        this IServiceCollection services,
        Func<HttpContext, Task<bool>> authFilter,
        Action<DashboardOptions>? configure = null)
    {
        return services.AddStyloFlowDashboard(options =>
        {
            options.AuthorizationFilter = authFilter;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Add StyloFlow Dashboard services with custom event store implementation.
    /// </summary>
    public static IServiceCollection AddStyloFlowDashboard<TEventStore>(
        this IServiceCollection services,
        Action<DashboardOptions>? configure = null)
        where TEventStore : class, IDashboardEventStore
    {
        var options = new DashboardOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSignalR();
        services.AddSingleton<IDashboardEventStore, TEventStore>();
        services.AddHostedService<DashboardBroadcaster>();

        return services;
    }

    /// <summary>
    /// Use StyloFlow Dashboard middleware and map SignalR hub.
    /// </summary>
    public static IApplicationBuilder UseStyloFlowDashboard(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<DashboardOptions>();

        if (!options.Enabled)
            return app;

        app.UseMiddleware<DashboardMiddleware>();

        return app;
    }

    /// <summary>
    /// Map the StyloFlow Dashboard SignalR hub endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapStyloFlowDashboardHub(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<DashboardOptions>();

        if (options.Enabled)
        {
            endpoints.MapHub<DashboardHub>(options.HubPath);
        }

        return endpoints;
    }
}
