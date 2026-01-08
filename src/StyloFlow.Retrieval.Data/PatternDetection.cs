using System.Text.RegularExpressions;

namespace StyloFlow.Retrieval.Data;

/// <summary>
/// Pattern detection utilities for structured data.
/// Detects text patterns, distribution types, trends, and relationships.
/// Uses source-generated regex patterns for optimal performance.
/// </summary>
public static partial class PatternDetection
{
    #region Text Pattern Detection

    /// <summary>
    /// Common text pattern types.
    /// </summary>
    public enum TextPatternType
    {
        Unknown,
        Email,
        Url,
        Uuid,
        Phone,
        IpAddress,
        CreditCard,
        Percentage,
        Currency,
        Date,
        Novel
    }

    #region Source-Generated Regex Patterns

    [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
    private static partial Regex EmailPatternRegex();

    [GeneratedRegex(@"^https?://[^\s]+$")]
    private static partial Regex UrlPatternRegex();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex UuidPatternRegex();

    [GeneratedRegex(@"^[\+]?[(]?[0-9]{1,3}[)]?[-\s\.]?[0-9]{3}[-\s\.]?[0-9]{4,6}$")]
    private static partial Regex PhonePatternRegex();

    [GeneratedRegex(@"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$")]
    private static partial Regex IpAddressPatternRegex();

    [GeneratedRegex(@"^[0-9]{4}[-\s]?[0-9]{4}[-\s]?[0-9]{4}[-\s]?[0-9]{1,7}$")]
    private static partial Regex CreditCardPatternRegex();

    [GeneratedRegex(@"^-?[0-9]+\.?[0-9]*\s*%$")]
    private static partial Regex PercentagePatternRegex();

    [GeneratedRegex(@"^[$£€¥][0-9,]+\.?[0-9]*$")]
    private static partial Regex CurrencyPatternRegex();

    [GeneratedRegex(@"^(19|20)\d{2}[-/](0[1-9]|1[0-2])[-/](0[1-9]|[12]\d|3[01])$")]
    private static partial Regex DatePatternRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplitRegex();

    #endregion

    private static readonly Dictionary<TextPatternType, Regex> Patterns = new()
    {
        [TextPatternType.Email] = EmailPatternRegex(),
        [TextPatternType.Url] = UrlPatternRegex(),
        [TextPatternType.Uuid] = UuidPatternRegex(),
        [TextPatternType.Phone] = PhonePatternRegex(),
        [TextPatternType.IpAddress] = IpAddressPatternRegex(),
        [TextPatternType.CreditCard] = CreditCardPatternRegex(),
        [TextPatternType.Percentage] = PercentagePatternRegex(),
        [TextPatternType.Currency] = CurrencyPatternRegex(),
        [TextPatternType.Date] = DatePatternRegex()
    };

    /// <summary>
    /// Detect text patterns in a collection of values.
    /// </summary>
    public static List<TextPatternMatch> DetectTextPatterns(IEnumerable<string?> values, double minMatchPercent = 0.1)
    {
        var nonNull = values.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList();
        if (nonNull.Count == 0) return new List<TextPatternMatch>();

        var matches = new List<TextPatternMatch>();

        foreach (var (patternType, regex) in Patterns)
        {
            var matchCount = nonNull.Count(v => regex.IsMatch(v));
            var matchPercent = (double)matchCount / nonNull.Count;

            if (matchPercent >= minMatchPercent)
            {
                matches.Add(new TextPatternMatch
                {
                    PatternType = patternType,
                    MatchCount = matchCount,
                    MatchPercent = matchPercent,
                    Examples = nonNull.Where(v => regex.IsMatch(v)).Take(3).ToList()
                });
            }
        }

        return matches.OrderByDescending(m => m.MatchPercent).ToList();
    }

    /// <summary>
    /// Detect novel pattern from character class structure.
    /// Returns pattern if >70% of values share similar structure.
    /// </summary>
    public static TextPatternMatch? DetectNovelPattern(IEnumerable<string?> values)
    {
        var nonNull = values.Where(v => !string.IsNullOrEmpty(v) && v!.Length is > 1 and < 100)
            .Select(v => v!)
            .Take(200)
            .ToList();

        if (nonNull.Count < 10) return null;

        // Convert to character class patterns (A=alpha, N=number, S=special, W=whitespace)
        var patterns = nonNull.Select(GetCharClassPattern).GroupBy(p => p)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (patterns.Count == 0) return null;

        var topPattern = patterns.First();
        var dominance = (double)topPattern.Count() / nonNull.Count;

        if (dominance < 0.7) return null;

        return new TextPatternMatch
        {
            PatternType = TextPatternType.Novel,
            MatchCount = topPattern.Count(),
            MatchPercent = dominance,
            DetectedRegex = CharPatternToRegex(topPattern.Key),
            Description = $"Consistent format: {DescribeCharPattern(topPattern.Key)}",
            Examples = topPattern.Take(3).ToList()
        };
    }

    private static string GetCharClassPattern(string value)
    {
        var pattern = new System.Text.StringBuilder();
        char? lastClass = null;

        foreach (var c in value)
        {
            var charClass = char.IsLetter(c) ? 'A' : char.IsDigit(c) ? 'N' : char.IsWhiteSpace(c) ? 'W' : 'S';
            if (charClass != lastClass)
            {
                pattern.Append(charClass);
                lastClass = charClass;
            }
        }

        return pattern.ToString();
    }

    private static string CharPatternToRegex(string charPattern)
    {
        var regex = new System.Text.StringBuilder("^");
        foreach (var c in charPattern)
        {
            regex.Append(c switch
            {
                'A' => "[a-zA-Z]+",
                'N' => "[0-9]+",
                'S' => "[^a-zA-Z0-9\\s]+",
                'W' => "\\s+",
                _ => "."
            });
        }
        return regex.Append('$').ToString();
    }

    private static string DescribeCharPattern(string charPattern)
    {
        var parts = new List<string>();
        foreach (var c in charPattern)
        {
            var desc = c switch { 'A' => "letters", 'N' => "numbers", 'S' => "symbols", 'W' => "space", _ => "?" };
            if (parts.Count == 0 || parts[^1] != desc) parts.Add(desc);
        }
        return string.Join(" + ", parts);
    }

    #endregion

    #region Distribution Classification

    public enum DistributionType
    {
        Unknown,
        Normal,
        Uniform,
        RightSkewed,
        LeftSkewed,
        Exponential,
        PowerLaw,
        Bimodal
    }

    /// <summary>
    /// Classify distribution type from statistics.
    /// </summary>
    public static DistributionType ClassifyDistribution(
        double skewness,
        double kurtosis,
        double? iqrRatio = null)
    {
        // Normal: skewness near 0, kurtosis near 3
        if (Math.Abs(skewness) < 0.5 && Math.Abs(kurtosis - 3) < 1)
            return DistributionType.Normal;

        // Uniform: low kurtosis, IQR ~50% of range
        if (kurtosis < 2 && iqrRatio.HasValue && iqrRatio > 0.4 && iqrRatio < 0.6)
            return DistributionType.Uniform;

        // Power law: very high skewness and kurtosis
        if (skewness > 2 && kurtosis > 10)
            return DistributionType.PowerLaw;

        // Exponential: right-skewed with high kurtosis
        if (skewness > 0.5 && kurtosis > 6)
            return DistributionType.Exponential;

        // Skewed
        if (skewness > 1) return DistributionType.RightSkewed;
        if (skewness < -1) return DistributionType.LeftSkewed;

        return DistributionType.Unknown;
    }

    /// <summary>
    /// Detect bimodality from histogram bin counts.
    /// </summary>
    public static bool DetectBimodality(IReadOnlyList<int> binCounts)
    {
        if (binCounts.Count < 3) return false;

        var peaks = 0;
        for (int i = 1; i < binCounts.Count - 1; i++)
        {
            if (binCounts[i] > binCounts[i - 1] && binCounts[i] > binCounts[i + 1])
                peaks++;
        }

        return peaks >= 2;
    }

    #endregion

    #region Trend Detection

    public enum TrendDirection { None, Increasing, Decreasing }

    /// <summary>
    /// Detect trend using simple linear regression.
    /// </summary>
    public static TrendInfo? DetectTrend(IReadOnlyList<double> values, double minRSquared = 0.3)
    {
        if (values.Count < 3) return null;

        var n = values.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;
        var sumY2 = 0.0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumX2 += i * i;
            sumY2 += values[i] * values[i];
        }

        var denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 1e-10) return null;

        var slope = (n * sumXY - sumX * sumY) / denominator;
        var meanY = sumY / n;

        // R-squared
        var ssTotal = sumY2 - n * meanY * meanY;
        var ssTrend = slope * slope * (sumX2 - n * (sumX / n) * (sumX / n));
        var rSquared = ssTotal > 0 ? ssTrend / ssTotal : 0;

        if (rSquared < minRSquared && Math.Abs(slope) < 0.001) return null;

        return new TrendInfo
        {
            Direction = slope > 0.001 ? TrendDirection.Increasing
                      : slope < -0.001 ? TrendDirection.Decreasing
                      : TrendDirection.None,
            Slope = slope,
            RSquared = Math.Max(0, Math.Min(1, rSquared))
        };
    }

    #endregion

    #region Periodicity Detection

    /// <summary>
    /// Detect periodicity using autocorrelation.
    /// </summary>
    public static PeriodicityInfo? DetectPeriodicity(
        IReadOnlyList<double> values,
        int maxLag = 50,
        double minConfidence = 0.2)
    {
        if (values.Count < maxLag * 2) return null;

        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean));
        if (variance < 1e-10) return null;

        var acfValues = new List<(int Lag, double Acf)>();

        for (int lag = 1; lag <= maxLag && lag < values.Count / 2; lag++)
        {
            var covariance = 0.0;
            for (int i = 0; i < values.Count - lag; i++)
            {
                covariance += (values[i] - mean) * (values[i + lag] - mean);
            }
            var acf = covariance / variance;
            acfValues.Add((lag, acf));
        }

        // Find peaks in ACF
        var peaks = new List<(int Lag, double Acf)>();
        for (int i = 1; i < acfValues.Count - 1; i++)
        {
            var (_, curr) = acfValues[i];
            var prev = acfValues[i - 1].Acf;
            var next = acfValues[i + 1].Acf;

            if (curr > prev && curr > next && curr > minConfidence)
                peaks.Add(acfValues[i]);
        }

        if (peaks.Count == 0) return null;

        var dominantPeak = peaks.OrderByDescending(p => p.Acf).First();

        return new PeriodicityInfo
        {
            HasPeriodicity = true,
            DominantPeriod = dominantPeak.Lag,
            Confidence = dominantPeak.Acf,
            Interpretation = InterpretPeriod(dominantPeak.Lag)
        };
    }

    private static string InterpretPeriod(int period) => period switch
    {
        7 => "Weekly cycle",
        12 => "Monthly cycle (yearly in monthly data)",
        24 => "Daily cycle (hourly data)",
        52 => "Yearly cycle (weekly data)",
        365 => "Yearly cycle (daily data)",
        _ when period <= 3 => $"Short cycle ({period} periods)",
        _ => $"Cycle ({period} periods)"
    };

    #endregion
}

#region Result Models

public class TextPatternMatch
{
    public PatternDetection.TextPatternType PatternType { get; set; }
    public int MatchCount { get; set; }
    public double MatchPercent { get; set; }
    public string? DetectedRegex { get; set; }
    public string? Description { get; set; }
    public List<string> Examples { get; set; } = new();
}

public class TrendInfo
{
    public PatternDetection.TrendDirection Direction { get; set; }
    public double Slope { get; set; }
    public double RSquared { get; set; }
}

public class PeriodicityInfo
{
    public bool HasPeriodicity { get; set; }
    public int DominantPeriod { get; set; }
    public double Confidence { get; set; }
    public string Interpretation { get; set; } = "";
}

#endregion
