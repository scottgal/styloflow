using StyloFlow.WorkflowBuilder.Extensions;
using StyloFlow.WorkflowBuilder.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWorkflowBuilder();

var app = builder.Build();

// Initialize storage
var storage = app.Services.GetRequiredService<WorkflowStorage>();
await storage.InitializeAsync();

app.UseWorkflowBuilder();
app.MapWorkflowBuilderHub();

app.MapGet("/", () => Results.Redirect("/workflow-builder/"));

Console.WriteLine("StyloFlow Workflow Builder running at http://localhost:5000/workflow-builder/");
Console.WriteLine("Make sure Ollama is running with TinyLlama: ollama run tinyllama");

app.Run("http://localhost:5000");
