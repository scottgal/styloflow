using System.Reflection;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using Microsoft.Extensions.Logging;
using StyloFlow.WorkflowBuilder.Atoms;

namespace StyloFlow.WorkflowBuilder.Runtime;

/// <summary>
/// Auto-discovers and registers atom executors using reflection.
/// Atoms are discovered by:
/// 1. Having a static `ExecuteAsync(WorkflowAtomContext)` method
/// 2. Having a static `Contract` property of type `AtomContract`
///
/// The name is taken from the Contract or can be overridden via manifest matching.
/// </summary>
public sealed class AtomExecutorRegistry
{
    private readonly Dictionary<string, Func<WorkflowAtomContext, Task>> _executors = new();
    private readonly Dictionary<string, AtomContract> _contracts = new();
    private readonly ILogger<AtomExecutorRegistry>? _logger;

    public IReadOnlyDictionary<string, Func<WorkflowAtomContext, Task>> Executors => _executors;
    public IReadOnlyDictionary<string, AtomContract> Contracts => _contracts;
    public int Count => _executors.Count;

    public AtomExecutorRegistry(ILogger<AtomExecutorRegistry>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discover atoms from the specified assemblies.
    /// If none provided, scans the calling assembly.
    /// </summary>
    public void DiscoverAtoms(params Assembly[] assemblies)
    {
        if (assemblies.Length == 0)
        {
            assemblies = [Assembly.GetCallingAssembly()];
        }

        foreach (var assembly in assemblies)
        {
            DiscoverFromAssembly(assembly);
        }

        _logger?.LogInformation("Discovered {Count} atom executors", _executors.Count);
    }

    /// <summary>
    /// Manually register an executor (for testing or custom atoms).
    /// </summary>
    public void Register(string name, Func<WorkflowAtomContext, Task> executor, AtomContract? contract = null)
    {
        _executors[name] = executor;
        if (contract != null)
        {
            _contracts[name] = contract;
        }
        _logger?.LogDebug("Registered executor: {Name}", name);
    }

    /// <summary>
    /// Get an executor by manifest name.
    /// </summary>
    public bool TryGetExecutor(string manifestName, out Func<WorkflowAtomContext, Task>? executor)
    {
        return _executors.TryGetValue(manifestName, out executor);
    }

    /// <summary>
    /// Get contract metadata for an atom.
    /// </summary>
    public AtomContract? GetContract(string manifestName)
    {
        return _contracts.TryGetValue(manifestName, out var contract) ? contract : null;
    }

    private void DiscoverFromAssembly(Assembly assembly)
    {
        var atomTypes = assembly.GetTypes()
            .Where(IsAtomType)
            .ToList();

        foreach (var type in atomTypes)
        {
            try
            {
                RegisterAtomType(type);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to register atom type: {Type}", type.FullName);
            }
        }
    }

    private static bool IsAtomType(Type type)
    {
        if (!type.IsClass || type.IsAbstract) return false;

        // Must have static ExecuteAsync method
        var executeMethod = type.GetMethod("ExecuteAsync",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(WorkflowAtomContext)],
            null);

        return executeMethod != null && executeMethod.ReturnType == typeof(Task);
    }

    private void RegisterAtomType(Type type)
    {
        // Get ExecuteAsync method
        var executeMethod = type.GetMethod("ExecuteAsync",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(WorkflowAtomContext)],
            null)!;

        // Create delegate
        var executor = (Func<WorkflowAtomContext, Task>)Delegate.CreateDelegate(
            typeof(Func<WorkflowAtomContext, Task>),
            executeMethod);

        // Try to get Contract for name
        string name;
        AtomContract? contract = null;

        var contractProperty = type.GetProperty("Contract",
            BindingFlags.Public | BindingFlags.Static);

        if (contractProperty != null && contractProperty.PropertyType == typeof(AtomContract))
        {
            contract = (AtomContract?)contractProperty.GetValue(null);
            name = contract?.Name ?? DeriveName(type);
        }
        else
        {
            name = DeriveName(type);
        }

        _executors[name] = executor;
        if (contract != null)
        {
            _contracts[name] = contract;
        }

        _logger?.LogDebug("Discovered atom: {Name} ({Type})", name, type.Name);
    }

    /// <summary>
    /// Derive atom name from type name if no Contract is available.
    /// FooBarAtom -> foo-bar
    /// </summary>
    private static string DeriveName(Type type)
    {
        var name = type.Name;

        // Remove common suffixes
        foreach (var suffix in new[] { "Atom", "Sensor", "Extractor", "Proposer", "Constrainer", "Renderer", "Shaper" })
        {
            if (name.EndsWith(suffix))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        // Convert PascalCase to kebab-case
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
