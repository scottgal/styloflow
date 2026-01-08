using System.Reflection;
using System.Text.Json;
using StyloFlow.WorkflowBuilder.Models;

namespace StyloFlow.WorkflowBuilder.Services;

/// <summary>
/// Loads sample workflows from embedded resources at startup.
/// </summary>
public class SampleWorkflowLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Load all sample workflows from embedded resources.
    /// </summary>
    public static IEnumerable<WorkflowDefinition> LoadEmbeddedSamples()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains("SampleWorkflows") && name.EndsWith(".json"))
            .OrderBy(name => name);

        foreach (var resourceName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions);
            if (workflow != null)
            {
                yield return workflow;
            }
        }
    }

    /// <summary>
    /// Load sample workflows from file system (development mode).
    /// </summary>
    public static IEnumerable<WorkflowDefinition> LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(f => f))
        {
            var json = File.ReadAllText(file);
            var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions);
            if (workflow != null)
            {
                yield return workflow;
            }
        }
    }
}
