namespace StyloFlow.Retrieval.Data;

/// <summary>
/// Multi-component anomaly scoring for data quality assessment.
/// Computes overall anomaly score (0-1) based on weighted components:
/// - Data quality issues
/// - Null rates
/// - Outliers
/// - Distribution anomalies
/// - Cardinality issues
/// - Schema concerns
/// </summary>
public static class AnomalyScoring
{
    /// <summary>
    /// Compute anomaly score for a dataset profile.
    /// </summary>
    public static AnomalyScoreResult ComputeScore(DatasetProfile profile)
    {
        var result = new AnomalyScoreResult { ProfileSource = profile.Source };
        var components = new List<(string Name, double Score, double Weight)>();

        // 1. Null rate score
        var nullScore = ComputeNullScore(profile);
        components.Add(("NullRate", nullScore.Score, 0.20));
        if (nullScore.Issues.Count > 0) result.Issues.AddRange(nullScore.Issues);

        // 2. Outlier score
        var outlierScore = ComputeOutlierScore(profile);
        components.Add(("Outliers", outlierScore.Score, 0.25));
        if (outlierScore.Issues.Count > 0) result.Issues.AddRange(outlierScore.Issues);

        // 3. Distribution score
        var distScore = ComputeDistributionScore(profile);
        components.Add(("Distribution", distScore.Score, 0.20));
        if (distScore.Issues.Count > 0) result.Issues.AddRange(distScore.Issues);

        // 4. Cardinality score
        var cardScore = ComputeCardinalityScore(profile);
        components.Add(("Cardinality", cardScore.Score, 0.15));
        if (cardScore.Issues.Count > 0) result.Issues.AddRange(cardScore.Issues);

        // 5. Schema score
        var schemaScore = ComputeSchemaScore(profile);
        components.Add(("Schema", schemaScore.Score, 0.20));
        if (schemaScore.Issues.Count > 0) result.Issues.AddRange(schemaScore.Issues);

        // Weighted average
        var totalWeight = components.Sum(c => c.Weight);
        result.OverallScore = Math.Round(components.Sum(c => c.Score * c.Weight) / totalWeight, 4);

        // Add component breakdown
        foreach (var (name, score, weight) in components)
        {
            result.Components.Add(new AnomalyComponent
            {
                Name = name,
                Score = Math.Round(score, 4),
                Weight = weight,
                WeightedScore = Math.Round(score * weight / totalWeight, 4)
            });
        }

        // Interpretation
        result.Interpretation = result.OverallScore switch
        {
            < 0.1 => "Excellent",
            < 0.2 => "Good",
            < 0.35 => "Fair",
            < 0.5 => "Concerning",
            < 0.7 => "Poor",
            _ => "Critical"
        };

        result.Recommendations = GenerateRecommendations(result);
        return result;
    }

    private static (double Score, List<AnomalyIssue> Issues) ComputeNullScore(DatasetProfile profile)
    {
        var issues = new List<AnomalyIssue>();
        if (profile.Columns.Count == 0) return (0, issues);

        var avgNullPercent = profile.Columns.Average(c => c.NullPercent);
        var maxNullPercent = profile.Columns.Max(c => c.NullPercent);
        var highNullColumns = profile.Columns.Where(c => c.NullPercent > 20).ToList();

        var score = avgNullPercent / 100.0 * 0.3 + maxNullPercent / 100.0 * 0.4 +
                   (double)highNullColumns.Count / profile.Columns.Count * 0.3;

        if (highNullColumns.Count > 0)
        {
            issues.Add(new AnomalyIssue
            {
                Category = "NullRate",
                Description = $"{highNullColumns.Count} columns with >20% null values (avg: {avgNullPercent:F1}%)",
                Severity = maxNullPercent > 50 ? IssueSeverity.High : IssueSeverity.Medium,
                AffectedColumns = highNullColumns.Select(c => c.Name).ToList()
            });
        }

        return (Math.Min(1.0, score), issues);
    }

    private static (double Score, List<AnomalyIssue> Issues) ComputeOutlierScore(DatasetProfile profile)
    {
        var issues = new List<AnomalyIssue>();
        var numericCols = profile.Columns.Where(c => c.IsNumeric).ToList();
        if (numericCols.Count == 0) return (0, issues);

        var outlierRatios = numericCols
            .Where(c => c.OutlierCount > 0 && c.RowCount > 0)
            .Select(c => (Column: c, Ratio: (double)c.OutlierCount / c.RowCount))
            .ToList();

        if (outlierRatios.Count == 0) return (0, issues);

        var avgRatio = outlierRatios.Average(o => o.Ratio);
        var maxRatio = outlierRatios.Max(o => o.Ratio);
        var highOutlierCols = outlierRatios.Where(o => o.Ratio > 0.05).ToList();

        var score = Math.Min(1.0, avgRatio * 10) * 0.4 + Math.Min(1.0, maxRatio * 5) * 0.4 +
                   (double)highOutlierCols.Count / numericCols.Count * 0.2;

        if (highOutlierCols.Count > 0)
        {
            var worstCols = highOutlierCols.OrderByDescending(o => o.Ratio).Take(3).ToList();
            issues.Add(new AnomalyIssue
            {
                Category = "Outliers",
                Description = $"{highOutlierCols.Count} columns with >5% outliers. Worst: {string.Join(", ", worstCols.Select(o => $"{o.Column.Name} ({o.Ratio:P1})"))}",
                Severity = maxRatio > 0.1 ? IssueSeverity.High : IssueSeverity.Medium,
                AffectedColumns = worstCols.Select(o => o.Column.Name).ToList()
            });
        }

        return (Math.Min(1.0, score), issues);
    }

    private static (double Score, List<AnomalyIssue> Issues) ComputeDistributionScore(DatasetProfile profile)
    {
        var issues = new List<AnomalyIssue>();
        var numericCols = profile.Columns.Where(c => c.IsNumeric && c.Skewness.HasValue).ToList();
        if (numericCols.Count == 0) return (0, issues);

        var highSkewCols = numericCols.Where(c => Math.Abs(c.Skewness ?? 0) > 2).ToList();
        var extremeSkewCols = numericCols.Where(c => Math.Abs(c.Skewness ?? 0) > 5).ToList();
        var highKurtosisCols = numericCols.Where(c => Math.Abs(c.Kurtosis ?? 0) > 7).ToList();

        var score = (double)highSkewCols.Count / numericCols.Count * 0.3 +
                   (double)extremeSkewCols.Count / numericCols.Count * 0.4 +
                   (double)highKurtosisCols.Count / numericCols.Count * 0.3;

        if (highSkewCols.Count > 0)
        {
            issues.Add(new AnomalyIssue
            {
                Category = "Distribution",
                Description = $"{highSkewCols.Count} columns are highly skewed (|skewness| > 2)",
                Severity = extremeSkewCols.Count > 0 ? IssueSeverity.Medium : IssueSeverity.Low,
                AffectedColumns = highSkewCols.Select(c => c.Name).ToList()
            });
        }

        return (Math.Min(1.0, score), issues);
    }

    private static (double Score, List<AnomalyIssue> Issues) ComputeCardinalityScore(DatasetProfile profile)
    {
        var issues = new List<AnomalyIssue>();
        if (profile.Columns.Count == 0) return (0, issues);

        var constantCols = profile.Columns.Where(c => c.UniqueCount <= 1).ToList();
        var nearUniqueCols = profile.Columns.Where(c => c.UniquePercent > 95 && !c.IsIdentifier).ToList();

        var score = (double)constantCols.Count / profile.Columns.Count * 0.5 +
                   (double)nearUniqueCols.Count / profile.Columns.Count * 0.3;

        if (constantCols.Count > 0)
        {
            issues.Add(new AnomalyIssue
            {
                Category = "Cardinality",
                Description = $"{constantCols.Count} constant columns (provide no information)",
                Severity = IssueSeverity.Medium,
                AffectedColumns = constantCols.Select(c => c.Name).ToList()
            });
        }

        if (nearUniqueCols.Count > 0)
        {
            issues.Add(new AnomalyIssue
            {
                Category = "Cardinality",
                Description = $"{nearUniqueCols.Count} near-unique columns (may be IDs)",
                Severity = IssueSeverity.Low,
                AffectedColumns = nearUniqueCols.Select(c => c.Name).ToList()
            });
        }

        return (Math.Min(1.0, score), issues);
    }

    private static (double Score, List<AnomalyIssue> Issues) ComputeSchemaScore(DatasetProfile profile)
    {
        var issues = new List<AnomalyIssue>();
        var score = 0.0;

        // Very wide datasets
        if (profile.ColumnCount > 100)
        {
            score += 0.2;
            issues.Add(new AnomalyIssue
            {
                Category = "Schema",
                Description = $"Wide dataset ({profile.ColumnCount} columns) - may have redundant features",
                Severity = profile.ColumnCount > 500 ? IssueSeverity.Medium : IssueSeverity.Low
            });
        }

        // High column-to-row ratio
        if (profile.RowCount > 0 && profile.ColumnCount / (double)profile.RowCount > 0.1)
        {
            score += 0.3;
            issues.Add(new AnomalyIssue
            {
                Category = "Schema",
                Description = $"High column-to-row ratio ({profile.ColumnCount} cols / {profile.RowCount} rows)",
                Severity = IssueSeverity.Medium
            });
        }

        return (Math.Min(1.0, score), issues);
    }

    private static List<string> GenerateRecommendations(AnomalyScoreResult result)
    {
        var recs = new List<string>();

        if (result.OverallScore >= 0.5)
            recs.Add("Data quality requires attention before modeling");

        var nullIssues = result.Issues.Where(i => i.Category == "NullRate" && i.Severity != IssueSeverity.Low).ToList();
        if (nullIssues.Count > 0)
            recs.Add($"Address high null rates in: {string.Join(", ", nullIssues.SelectMany(i => i.AffectedColumns).Take(5))}");

        var outlierIssues = result.Issues.Where(i => i.Category == "Outliers" && i.Severity == IssueSeverity.High).ToList();
        if (outlierIssues.Count > 0)
            recs.Add("Investigate outliers - consider capping, winsorization, or separate modeling");

        if (result.Issues.Any(i => i.Category == "Cardinality" && i.Description.Contains("constant")))
            recs.Add("Remove constant columns before modeling");

        if (result.Issues.Any(i => i.Category == "Distribution"))
            recs.Add("Consider transformations (log, Box-Cox) for highly skewed columns");

        if (recs.Count == 0)
            recs.Add("Data quality is acceptable for analysis");

        return recs;
    }
}

#region Models

public class AnomalyScoreResult
{
    public string ProfileSource { get; set; } = "";
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    public double OverallScore { get; set; }
    public string Interpretation { get; set; } = "";
    public List<AnomalyComponent> Components { get; set; } = new();
    public List<AnomalyIssue> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

public class AnomalyComponent
{
    public string Name { get; set; } = "";
    public double Score { get; set; }
    public double Weight { get; set; }
    public double WeightedScore { get; set; }
}

public class AnomalyIssue
{
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public IssueSeverity Severity { get; set; } = IssueSeverity.Low;
    public List<string> AffectedColumns { get; set; } = new();
}

public enum IssueSeverity { Low, Medium, High, Critical }

/// <summary>
/// Dataset profile for anomaly scoring.
/// </summary>
public class DatasetProfile
{
    public string Source { get; set; } = "";
    public int RowCount { get; set; }
    public int ColumnCount => Columns.Count;
    public List<ColumnStats> Columns { get; set; } = new();
}

/// <summary>
/// Column statistics for anomaly scoring.
/// </summary>
public class ColumnStats
{
    public string Name { get; set; } = "";
    public bool IsNumeric { get; set; }
    public bool IsIdentifier { get; set; }
    public int RowCount { get; set; }
    public double NullPercent { get; set; }
    public int UniqueCount { get; set; }
    public double UniquePercent { get; set; }
    public int OutlierCount { get; set; }
    public double? Skewness { get; set; }
    public double? Kurtosis { get; set; }
}

#endregion
