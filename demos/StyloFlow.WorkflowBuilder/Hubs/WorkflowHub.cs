using Microsoft.AspNetCore.SignalR;
using StyloFlow.WorkflowBuilder.Models;

namespace StyloFlow.WorkflowBuilder.Hubs;

/// <summary>
/// SignalR hub for real-time workflow updates.
/// </summary>
public class WorkflowHub : Hub
{
    public async Task JoinWorkflow(string workflowId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"workflow:{workflowId}");
    }

    public async Task LeaveWorkflow(string workflowId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"workflow:{workflowId}");
    }

    public async Task BroadcastNodeAdded(string workflowId, WorkflowNode node)
    {
        await Clients.OthersInGroup($"workflow:{workflowId}").SendAsync("NodeAdded", node);
    }

    public async Task BroadcastNodeMoved(string workflowId, string nodeId, double x, double y)
    {
        await Clients.OthersInGroup($"workflow:{workflowId}").SendAsync("NodeMoved", nodeId, x, y);
    }

    public async Task BroadcastEdgeAdded(string workflowId, WorkflowEdge edge)
    {
        await Clients.OthersInGroup($"workflow:{workflowId}").SendAsync("EdgeAdded", edge);
    }

    public async Task BroadcastNodeRemoved(string workflowId, string nodeId)
    {
        await Clients.OthersInGroup($"workflow:{workflowId}").SendAsync("NodeRemoved", nodeId);
    }

    public async Task BroadcastEdgeRemoved(string workflowId, string edgeId)
    {
        await Clients.OthersInGroup($"workflow:{workflowId}").SendAsync("EdgeRemoved", edgeId);
    }
}
