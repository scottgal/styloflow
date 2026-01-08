using System.Text.Json;
using Microsoft.Extensions.Logging;
using StyloFlow.Licensing.Models;

namespace StyloFlow.Licensing.Services;

/// <summary>
/// Manages license validation, state, and enforcement.
/// </summary>
public sealed class LicenseManager : ILicenseManager
{
    private readonly StyloFlowOptions _options;
    private readonly ILogger<LicenseManager> _logger;

    private LicenseToken? _license;
    private LicenseState _state = LicenseState.Unknown;

    private static readonly IReadOnlyList<string> TierHierarchy = new[]
    {
        "free", "starter", "professional", "enterprise"
    };

    public LicenseManager(StyloFlowOptions options, ILogger<LicenseManager> logger)
    {
        _options = options;
        _logger = logger;
    }

    public LicenseState CurrentState => _state;

    public string CurrentTier => _license?.Tier ?? "free";

    public int MaxSlots =>
        _options.LicenseOverrides?.MaxSlots ??
        _license?.Limits.MaxMoleculeSlots ??
        _options.FreeTierMaxSlots;

    public int MaxWorkUnitsPerMinute =>
        _options.LicenseOverrides?.MaxWorkUnitsPerMinute ??
        _license?.Limits.MaxWorkUnitsPerMinute ??
        _options.FreeTierMaxWorkUnitsPerMinute;

    public int? MaxNodes =>
        _options.LicenseOverrides?.MaxNodes ??
        _license?.Limits.MaxNodes ??
        _options.FreeTierMaxNodes;

    public bool IsExpiringSoon
    {
        get
        {
            if (_license == null) return false;
            var effectiveExpiry = _options.LicenseOverrides?.Expiry ?? _license.Expiry;
            return effectiveExpiry - DateTimeOffset.UtcNow < _options.LicenseGracePeriod;
        }
    }

    public TimeSpan TimeUntilExpiry
    {
        get
        {
            if (_license == null) return TimeSpan.Zero;
            var effectiveExpiry = _options.LicenseOverrides?.Expiry ?? _license.Expiry;
            var remaining = effectiveExpiry - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public LicenseToken? CurrentLicense => _license;

    public IReadOnlyList<string> EnabledFeatures =>
        _options.LicenseOverrides?.Features?.AsReadOnly() ??
        _license?.Features ??
        Array.Empty<string>();

    public event EventHandler<LicenseStateChangedEvent>? LicenseStateChanged;

    public async Task<LicenseValidationResult> ValidateLicenseAsync(CancellationToken ct = default)
    {
        try
        {
            // If custom validator is provided, use it
            if (_options.CustomLicenseValidator != null)
            {
                var token = _options.LicenseToken ?? await LoadLicenseTokenAsync(ct);
                if (string.IsNullOrEmpty(token))
                {
                    return SetState(LicenseState.FreeTier, LicenseValidationResult.Failure("No license token found"));
                }

                var result = await _options.CustomLicenseValidator(token, ct);
                return SetState(result.Valid ? LicenseState.Valid : LicenseState.Invalid, result);
            }

            // Default validation
            var licenseJson = _options.LicenseToken ?? await LoadLicenseTokenAsync(ct);
            if (string.IsNullOrEmpty(licenseJson))
            {
                _logger.LogInformation("No license found, running in free tier");
                return SetState(LicenseState.FreeTier, LicenseValidationResult.Failure("No license found - using free tier"));
            }

            // Parse the license
            var license = ParseLicense(licenseJson);
            if (license == null)
            {
                return SetState(LicenseState.Invalid, LicenseValidationResult.Failure("Failed to parse license"));
            }

            // Verify signature (if vendor key provided)
            if (!string.IsNullOrEmpty(_options.VendorPublicKey) && !string.IsNullOrEmpty(license.Signature))
            {
                if (!VerifySignature(licenseJson, license.Signature, _options.VendorPublicKey))
                {
                    _logger.LogWarning("License signature verification failed");
                    return SetState(LicenseState.Invalid, LicenseValidationResult.Failure("Invalid signature"));
                }
            }

            // Check expiry
            var effectiveExpiry = _options.LicenseOverrides?.Expiry ?? license.Expiry;
            if (DateTimeOffset.UtcNow >= effectiveExpiry)
            {
                _logger.LogWarning("License has expired");
                return SetState(LicenseState.Expired, LicenseValidationResult.Failure("License expired"));
            }

            // License is valid
            _license = license;
            var state = IsExpiringSoon ? LicenseState.ExpiringSoon : LicenseState.Valid;
            _logger.LogInformation("License validated: {Tier}, expires {Expiry}", license.Tier, effectiveExpiry);

            return SetState(state, LicenseValidationResult.Success(license));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating license");
            return SetState(LicenseState.FreeTier, LicenseValidationResult.Failure($"Validation error: {ex.Message}"));
        }
    }

    public bool HasFeature(string feature)
    {
        if (string.IsNullOrEmpty(feature)) return true;

        var features = EnabledFeatures;

        // Check for wildcard
        if (features.Contains("*")) return true;

        // Check exact match
        if (features.Contains(feature)) return true;

        // Check prefix match (e.g., "detector.*" matches "detector.bot")
        foreach (var f in features)
        {
            if (f.EndsWith(".*") && feature.StartsWith(f[..^2]))
                return true;
        }

        return false;
    }

    public bool MeetsTierRequirement(string requiredTier)
    {
        if (string.IsNullOrEmpty(requiredTier) || requiredTier == "free")
            return true;

        var currentIndex = TierHierarchy.IndexOf(CurrentTier.ToLowerInvariant());
        var requiredIndex = TierHierarchy.IndexOf(requiredTier.ToLowerInvariant());

        // Unknown tiers default to lowest
        if (currentIndex < 0) currentIndex = 0;
        if (requiredIndex < 0) requiredIndex = 0;

        return currentIndex >= requiredIndex;
    }

    private async Task<string?> LoadLicenseTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.LicenseFilePath))
            return null;

        if (!File.Exists(_options.LicenseFilePath))
        {
            _logger.LogDebug("License file not found: {Path}", _options.LicenseFilePath);
            return null;
        }

        return await File.ReadAllTextAsync(_options.LicenseFilePath, ct);
    }

    private LicenseToken? ParseLicense(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LicenseToken>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse license JSON");
            return null;
        }
    }

    private bool VerifySignature(string licenseJson, string signature, string publicKey)
    {
        try
        {
            var signingService = new Cryptography.LicenseSigningService(publicKey, isPublicKey: true);
            var isValid = signingService.VerifyLicense(licenseJson);

            if (!isValid)
            {
                _logger.LogWarning("License signature verification failed");
            }
            else
            {
                _logger.LogDebug("License signature verified successfully");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during signature verification");
            return false;
        }
    }

    private LicenseValidationResult SetState(LicenseState newState, LicenseValidationResult result)
    {
        var previousState = _state;
        _state = newState;

        if (previousState != newState)
        {
            var evt = new LicenseStateChangedEvent
            {
                PreviousState = previousState,
                NewState = newState,
                Reason = result.ErrorMessage
            };

            LicenseStateChanged?.Invoke(this, evt);
            _options.OnLicenseStateChanged?.Invoke(evt);

            if (_options.OnLicenseStateChangedAsync != null)
            {
                // Fire and forget async handler
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _options.OnLicenseStateChangedAsync(evt, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error in OnLicenseStateChangedAsync handler");
                    }
                });
            }
        }

        return result;
    }
}

file static class ListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(list[i], item))
                return i;
        }
        return -1;
    }
}
