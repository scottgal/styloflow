using System.Text.RegularExpressions;

namespace StyloFlow.Retrieval.Data;

/// <summary>
/// PII (Personally Identifiable Information) detection using regex patterns.
/// Detects common PII types: SSN, credit cards, emails, phones, addresses, etc.
/// Uses source-generated regex patterns for optimal performance.
/// </summary>
public static partial class PiiDetection
{
    /// <summary>
    /// Types of PII that can be detected.
    /// </summary>
    public enum PiiType
    {
        None,
        SSN,
        CreditCard,
        Email,
        PhoneNumber,
        Address,
        PersonName,
        IPAddress,
        MACAddress,
        DateOfBirth,
        PassportNumber,
        DriversLicense,
        BankAccount,
        RoutingNumber,
        UUID,
        URL,
        VIN,
        IBAN,
        ZipCode,
        Other
    }

    /// <summary>
    /// Risk levels for PII exposure.
    /// </summary>
    public enum PiiRiskLevel { None, Low, Medium, High, Critical }

    #region Source-Generated Regex Patterns

    [GeneratedRegex(@"^\d{3}-?\d{2}-?\d{4}$")]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"^(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13})$")]
    private static partial Regex CreditCardRegex();

    [GeneratedRegex(@"^\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}$")]
    private static partial Regex CreditCardFormattedRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^(\+1)?[\s.-]?\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}$")]
    private static partial Regex UsPhoneRegex();

    [GeneratedRegex(@"^\+?[1-9]\d{9,14}$")]
    private static partial Regex InternationalPhoneRegex();

    [GeneratedRegex(@"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$")]
    private static partial Regex IPv4Regex();

    [GeneratedRegex(@"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$")]
    private static partial Regex MacAddressRegex();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex UuidRegex();

    [GeneratedRegex(@"^https?://[^\s/$.?#].[^\s]*$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^(0[1-9]|1[0-2])/(0[1-9]|[12]\d|3[01])/(19|20)\d{2}$")]
    private static partial Regex DateMmDdYyyyRegex();

    [GeneratedRegex(@"^(19|20)\d{2}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$")]
    private static partial Regex DateYyyyMmDdRegex();

    [GeneratedRegex(@"^\d{5}(-\d{4})?$")]
    private static partial Regex ZipCodeRegex();

    [GeneratedRegex(@"^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.IgnoreCase)]
    private static partial Regex VinRegex();

    [GeneratedRegex(@"^[A-Z]{2}\d{2}[A-Z0-9]{4,30}$", RegexOptions.IgnoreCase)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"^[0-9]{9}$")]
    private static partial Regex RoutingNumberRegex();

    [GeneratedRegex(@"^\d{8,17}$")]
    private static partial Regex BankAccountRegex();

    #endregion

    private static readonly List<(PiiType Type, Regex Pattern, string Description)> Patterns = new()
    {
        (PiiType.SSN, SsnRegex(), "US Social Security Number"),
        (PiiType.CreditCard, CreditCardRegex(), "Credit Card"),
        (PiiType.CreditCard, CreditCardFormattedRegex(), "Credit Card (formatted)"),
        (PiiType.Email, EmailRegex(), "Email"),
        (PiiType.PhoneNumber, UsPhoneRegex(), "US Phone"),
        (PiiType.PhoneNumber, InternationalPhoneRegex(), "International Phone"),
        (PiiType.IPAddress, IPv4Regex(), "IPv4"),
        (PiiType.MACAddress, MacAddressRegex(), "MAC Address"),
        (PiiType.UUID, UuidRegex(), "UUID"),
        (PiiType.URL, UrlRegex(), "URL"),
        (PiiType.DateOfBirth, DateMmDdYyyyRegex(), "Date MM/DD/YYYY"),
        (PiiType.DateOfBirth, DateYyyyMmDdRegex(), "Date YYYY-MM-DD"),
        (PiiType.ZipCode, ZipCodeRegex(), "US Zip"),
        (PiiType.VIN, VinRegex(), "VIN"),
        (PiiType.IBAN, IbanRegex(), "IBAN"),
        (PiiType.RoutingNumber, RoutingNumberRegex(), "US Routing Number"),
        (PiiType.BankAccount, BankAccountRegex(), "Bank Account"),
    };

    /// <summary>
    /// Scan values for PII patterns.
    /// </summary>
    public static PiiScanResult ScanValues(string columnName, IEnumerable<string?> values)
    {
        var result = new PiiScanResult { ColumnName = columnName };

        var nonNull = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Take(1000).ToList();
        if (nonNull.Count == 0)
        {
            // Check column name even without values
            var nameHint = DetectFromColumnName(columnName);
            if (nameHint != PiiType.None)
            {
                result.PrimaryType = nameHint;
                result.Confidence = 0.3;
                result.IsPii = true;
                result.RiskLevel = PiiRiskLevel.Medium;
            }
            return result;
        }

        var detections = new Dictionary<PiiType, int>();
        var samples = new Dictionary<PiiType, List<string>>();

        foreach (var value in nonNull)
        {
            foreach (var (type, pattern, _) in Patterns)
            {
                if (pattern.IsMatch(value))
                {
                    detections.TryAdd(type, 0);
                    detections[type]++;

                    samples.TryAdd(type, new List<string>());
                    if (samples[type].Count < 3)
                        samples[type].Add(RedactValue(value, type));

                    break; // One match per value
                }
            }
        }

        if (detections.Count > 0)
        {
            result.DetectedTypes = detections
                .Where(d => (double)d.Value / nonNull.Count > 0.1)
                .Select(d => new PiiDetectionResult
                {
                    Type = d.Key,
                    MatchCount = d.Value,
                    MatchRate = (double)d.Value / nonNull.Count,
                    Samples = samples.GetValueOrDefault(d.Key, new List<string>())
                })
                .OrderByDescending(d => d.MatchRate)
                .ToList();

            if (result.DetectedTypes.Count > 0)
            {
                var top = result.DetectedTypes[0];
                result.PrimaryType = top.Type;
                result.Confidence = top.MatchRate;
                result.IsPii = top.MatchRate > 0.5;
                result.RiskLevel = ClassifyRisk(top.Type, top.MatchRate);
            }
        }

        // Also check column name
        var nameType = DetectFromColumnName(columnName);
        if (nameType != PiiType.None && result.PrimaryType == PiiType.None)
        {
            result.PrimaryType = nameType;
            result.Confidence = 0.3;
            result.IsPii = true;
            result.RiskLevel = PiiRiskLevel.Medium;
        }

        return result;
    }

    /// <summary>
    /// Detect PII type from column name.
    /// </summary>
    public static PiiType DetectFromColumnName(string columnName)
    {
        var lower = columnName.ToLowerInvariant();

        if (lower.Contains("ssn") || lower.Contains("social_security")) return PiiType.SSN;
        if (lower.Contains("credit") && (lower.Contains("card") || lower.Contains("number"))) return PiiType.CreditCard;
        if (lower.Contains("email") || lower.Contains("e_mail")) return PiiType.Email;
        if (lower.Contains("phone") || lower.Contains("mobile") || lower.Contains("cell")) return PiiType.PhoneNumber;
        if (lower.Contains("address") || lower.Contains("street")) return PiiType.Address;
        if (lower == "name" || lower.Contains("first_name") || lower.Contains("last_name")) return PiiType.PersonName;
        if (lower.Contains("ip_address") || lower == "ip") return PiiType.IPAddress;
        if (lower.Contains("dob") || lower.Contains("birth")) return PiiType.DateOfBirth;
        if (lower.Contains("passport")) return PiiType.PassportNumber;
        if (lower.Contains("license") || lower.Contains("dl_")) return PiiType.DriversLicense;

        return PiiType.None;
    }

    private static PiiRiskLevel ClassifyRisk(PiiType type, double confidence)
    {
        // High-risk PII
        if (type is PiiType.SSN or PiiType.CreditCard or PiiType.BankAccount or PiiType.PassportNumber)
            return confidence > 0.7 ? PiiRiskLevel.Critical : PiiRiskLevel.High;

        // Medium-risk PII
        if (type is PiiType.Email or PiiType.PhoneNumber or PiiType.DriversLicense or PiiType.DateOfBirth)
            return confidence > 0.7 ? PiiRiskLevel.High : PiiRiskLevel.Medium;

        return confidence > 0.7 ? PiiRiskLevel.Medium : PiiRiskLevel.Low;
    }

    /// <summary>
    /// Redact a value for safe display.
    /// </summary>
    public static string RedactValue(string value, PiiType type)
    {
        if (value.Length <= 4) return "****";

        return type switch
        {
            PiiType.SSN => "***-**-" + value[^4..],
            PiiType.CreditCard => "**** **** **** " + value[^4..],
            PiiType.Email => value[..2] + "***@***" + (value.Contains('@') ? value[value.LastIndexOf('.')..] : ""),
            PiiType.PhoneNumber => "***-***-" + value[^4..],
            _ => value[..2] + new string('*', Math.Min(value.Length - 4, 10)) + value[^2..]
        };
    }

    /// <summary>
    /// Get recommended action for a risk level.
    /// </summary>
    public static string GetRecommendedAction(PiiRiskLevel risk) => risk switch
    {
        PiiRiskLevel.Critical => "EXCLUDE from output or use heavy masking (hash)",
        PiiRiskLevel.High => "Mask or redact values",
        PiiRiskLevel.Medium => "Consider using Faker patterns for pseudonymization",
        PiiRiskLevel.Low => "Safe for synthetic generation with distribution matching",
        _ => "No action needed"
    };
}

#region Result Models

public class PiiScanResult
{
    public string ColumnName { get; set; } = "";
    public bool IsPii { get; set; }
    public PiiDetection.PiiType PrimaryType { get; set; }
    public double Confidence { get; set; }
    public PiiDetection.PiiRiskLevel RiskLevel { get; set; }
    public List<PiiDetectionResult> DetectedTypes { get; set; } = new();
}

public class PiiDetectionResult
{
    public PiiDetection.PiiType Type { get; set; }
    public int MatchCount { get; set; }
    public double MatchRate { get; set; }
    public List<string> Samples { get; set; } = new();
}

#endregion
