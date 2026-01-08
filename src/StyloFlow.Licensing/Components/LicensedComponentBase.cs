using Mostlylucid.Ephemeral;
using StyloFlow.Licensing.Services;

namespace StyloFlow.Licensing.Components;

/// <summary>
/// Base class for licensed components that provides common license-aware functionality.
/// Handles license validation, signal emission based on tier, and work unit metering.
/// </summary>
public abstract class LicensedComponentBase : ILicensedComponent
{
    private readonly ILicenseManager _licenseManager;
    private readonly IWorkUnitMeter _workUnitMeter;
    private readonly SignalSink _signalSink;
    private readonly LicenseRequirements _requirements;
    private readonly List<string> _deferOnSignals;
    private readonly List<string> _resumeOnSignals;
    private readonly List<string> _freeTierSignals;
    private readonly List<string> _licensedSignals;
    private bool? _isLicensedCache;

    protected LicensedComponentBase(
        ILicenseManager licenseManager,
        IWorkUnitMeter workUnitMeter,
        SignalSink signalSink,
        LicenseRequirements? requirements = null)
    {
        _licenseManager = licenseManager;
        _workUnitMeter = workUnitMeter;
        _signalSink = signalSink;
        _requirements = requirements ?? LicenseRequirements.FreeTier;

        // Default signals - can be overridden by derived classes
        _deferOnSignals = new List<string>();
        _resumeOnSignals = new List<string>();
        _freeTierSignals = new List<string>();
        _licensedSignals = new List<string>();

        // If we require system coordinator, defer on its ready signal
        if (_requirements.RequiresSystemCoordinator)
        {
            _deferOnSignals.Add(SystemCoordinator.Signals.Ready);
            _resumeOnSignals.Add(SystemCoordinator.Signals.Ready);
        }
    }

    /// <inheritdoc />
    public abstract string ComponentId { get; }

    /// <inheritdoc />
    public LicenseRequirements Requirements => _requirements;

    /// <inheritdoc />
    public IReadOnlyList<string> DeferOnSignals => _deferOnSignals;

    /// <inheritdoc />
    public IReadOnlyList<string> ResumeOnSignals => _resumeOnSignals;

    /// <inheritdoc />
    public IReadOnlyList<string> FreeTierSignals => _freeTierSignals;

    /// <inheritdoc />
    public IReadOnlyList<string> LicensedSignals => _licensedSignals;

    /// <inheritdoc />
    public bool IsLicensed
    {
        get
        {
            // Cache the result for performance
            _isLicensedCache ??= CheckLicense();
            return _isLicensedCache.Value;
        }
    }

    /// <summary>
    /// Gets the license manager.
    /// </summary>
    protected ILicenseManager LicenseManager => _licenseManager;

    /// <summary>
    /// Gets the work unit meter.
    /// </summary>
    protected IWorkUnitMeter WorkUnitMeter => _workUnitMeter;

    /// <summary>
    /// Gets the signal sink.
    /// </summary>
    protected SignalSink SignalSink => _signalSink;

    /// <summary>
    /// Add a signal to emit when in free tier mode.
    /// </summary>
    protected void AddFreeTierSignal(string signal)
    {
        _freeTierSignals.Add(signal);
    }

    /// <summary>
    /// Add a signal to emit when in licensed mode.
    /// </summary>
    protected void AddLicensedSignal(string signal)
    {
        _licensedSignals.Add(signal);
    }

    /// <summary>
    /// Add a signal to defer on.
    /// </summary>
    protected void AddDeferOnSignal(string signal)
    {
        _deferOnSignals.Add(signal);
    }

    /// <summary>
    /// Add a signal to resume on.
    /// </summary>
    protected void AddResumeOnSignal(string signal)
    {
        _resumeOnSignals.Add(signal);
    }

    /// <summary>
    /// Record work units for this operation.
    /// Automatically calculates based on data size if WorkUnitsPerKb is set.
    /// </summary>
    /// <param name="dataSizeBytes">Optional data size for proportional metering.</param>
    protected void RecordWorkUnits(long dataSizeBytes = 0)
    {
        var workUnits = _requirements.WorkUnits;

        if (dataSizeBytes > 0 && _requirements.WorkUnitsPerKb > 0)
        {
            var kb = dataSizeBytes / 1024.0;
            workUnits += kb * _requirements.WorkUnitsPerKb;
        }

        _workUnitMeter.Record(workUnits, ComponentId);
    }

    /// <summary>
    /// Check if we can consume the specified work units.
    /// </summary>
    /// <param name="dataSizeBytes">Optional data size for proportional calculation.</param>
    /// <returns>True if the operation should be allowed.</returns>
    protected bool CanPerformOperation(long dataSizeBytes = 0)
    {
        var workUnits = _requirements.WorkUnits;

        if (dataSizeBytes > 0 && _requirements.WorkUnitsPerKb > 0)
        {
            var kb = dataSizeBytes / 1024.0;
            workUnits += kb * _requirements.WorkUnitsPerKb;
        }

        return _workUnitMeter.CanConsume(workUnits);
    }

    /// <summary>
    /// Emit the appropriate signals based on current license status.
    /// </summary>
    protected void EmitModeSignals()
    {
        if (IsLicensed)
        {
            foreach (var signal in _licensedSignals)
            {
                _signalSink.Raise(signal);
            }
        }
        else
        {
            foreach (var signal in _freeTierSignals)
            {
                _signalSink.Raise(signal);
            }
        }
    }

    /// <summary>
    /// Emit a signal, automatically prefixed with component ID.
    /// </summary>
    /// <param name="signalSuffix">The signal suffix (e.g., "started", "completed").</param>
    /// <param name="key">Optional key for the signal.</param>
    protected void EmitSignal(string signalSuffix, string? key = null)
    {
        var signal = $"{ComponentId}.{signalSuffix}";
        _signalSink.Raise(signal, key);
    }

    /// <summary>
    /// Check if the current license meets requirements.
    /// Can be overridden for custom license logic.
    /// </summary>
    protected virtual bool CheckLicense()
    {
        // Check tier
        if (!_licenseManager.MeetsTierRequirement(_requirements.MinimumTier))
        {
            return false;
        }

        // Check required features
        foreach (var feature in _requirements.RequiredFeatures)
        {
            if (!_licenseManager.HasFeature(feature))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validate that the component can run.
    /// Throws if license requirements are not met and degradation is not allowed.
    /// </summary>
    /// <exception cref="LicenseRequiredException">Thrown if license is required but not valid.</exception>
    protected void ValidateLicense()
    {
        if (!IsLicensed && !_requirements.AllowFreeTierDegradation)
        {
            throw new LicenseRequiredException(
                $"Component '{ComponentId}' requires a '{_requirements.MinimumTier}' tier license.",
                _requirements.MinimumTier,
                _requirements.RequiredFeatures);
        }
    }

    /// <summary>
    /// Reset the license cache. Call this if the license state may have changed.
    /// </summary>
    protected void ResetLicenseCache()
    {
        _isLicensedCache = null;
    }
}

/// <summary>
/// Exception thrown when a license is required but not valid.
/// </summary>
public sealed class LicenseRequiredException : Exception
{
    public LicenseRequiredException(string message, string requiredTier, IReadOnlyList<string> requiredFeatures)
        : base(message)
    {
        RequiredTier = requiredTier;
        RequiredFeatures = requiredFeatures;
    }

    /// <summary>
    /// The minimum tier required.
    /// </summary>
    public string RequiredTier { get; }

    /// <summary>
    /// The features required.
    /// </summary>
    public IReadOnlyList<string> RequiredFeatures { get; }
}
