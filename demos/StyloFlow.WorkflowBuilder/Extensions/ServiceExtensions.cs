using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Mostlylucid.Ephemeral;
using StyloFlow.WorkflowBuilder.Hubs;
using StyloFlow.WorkflowBuilder.Middleware;
using StyloFlow.WorkflowBuilder.Runtime;
using StyloFlow.WorkflowBuilder.Services;

namespace StyloFlow.WorkflowBuilder.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddWorkflowBuilder(this IServiceCollection services, string? ollamaUrl = null)
    {
        // Core Ephemeral signal sink - shared across all coordinators
        var globalSink = new SignalSink(maxCapacity: 5000, maxAge: TimeSpan.FromMinutes(10));
        services.AddSingleton(globalSink);

        // Manifest service for YAML atom definitions
        var manifestService = new ManifestService();
        manifestService.LoadEmbeddedManifests();
        services.AddSingleton(manifestService);

        // Ollama LLM service
        var ollamaService = new OllamaService(ollamaUrl ?? "http://localhost:11434", "tinyllama");
        services.AddSingleton(ollamaService);

        // Workflow storage using Ephemeral's SqliteDataStorageAtom
        services.AddSingleton(sp => new WorkflowStorage(sp.GetRequiredService<SignalSink>(), "./data"));

        // Workflow store (in-memory for now)
        services.AddSingleton<IWorkflowStore, InMemoryWorkflowStore>();
        services.AddSingleton<WorkflowValidator>();

        // SignalR
        services.AddSignalR();

        // SignalR coordinator - singleton that broadcasts signals to connected clients
        services.AddSingleton(sp =>
        {
            var hubContext = sp.GetRequiredService<IHubContext<WorkflowHub>>();
            return new SignalRCoordinator(hubContext, globalSink);
        });

        // Workflow orchestrator - uses Ephemeral's EphemeralWorkCoordinator
        services.AddSingleton(sp => new WorkflowOrchestrator(
            sp.GetRequiredService<SignalSink>(),
            sp.GetRequiredService<OllamaService>(),
            sp.GetRequiredService<WorkflowStorage>(),
            sp.GetRequiredService<SignalRCoordinator>()));

        return services;
    }

    public static IApplicationBuilder UseWorkflowBuilder(this IApplicationBuilder app, string basePath = "/workflow-builder")
    {
        app.UseMiddleware<WorkflowBuilderMiddleware>(basePath);
        return app;
    }

    public static IEndpointRouteBuilder MapWorkflowBuilderHub(this IEndpointRouteBuilder endpoints, string hubPath = "/workflow-builder/hub")
    {
        endpoints.MapHub<WorkflowHub>(hubPath);
        return endpoints;
    }
}
