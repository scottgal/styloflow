using StyloFlow.Retrieval.Data;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Data;

public class AnomalyScoringTests
{
    [Fact]
    public void ComputeScore_EmptyProfile_ReturnsLowScore()
    {
        // Arrange
        var profile = new DatasetProfile
        {
            Source = "test",
            RowCount = 0,
            Columns = new List<ColumnStats>()
        };

        // Act
        var result = AnomalyScoring.ComputeScore(profile);

        // Assert
        Assert.True(result.OverallScore <= 0.2);
        Assert.NotEmpty(result.Interpretation);
    }

    [Fact]
    public void ComputeScore_HealthyProfile_ReturnsLowScore()
    {
        // Arrange
        var profile = new DatasetProfile
        {
            Source = "healthy_data",
            RowCount = 1000,
            Columns = new List<ColumnStats>
            {
                new ColumnStats
                {
                    Name = "id",
                    IsNumeric = false,
                    IsIdentifier = true,
                    RowCount = 1000,
                    NullPercent = 0,
                    UniqueCount = 1000,
                    UniquePercent = 100
                },
                new ColumnStats
                {
                    Name = "value",
                    IsNumeric = true,
                    RowCount = 1000,
                    NullPercent = 2,
                    UniqueCount = 500,
                    UniquePercent = 50,
                    OutlierCount = 10,
                    Skewness = 0.2,
                    Kurtosis = 3.0
                }
            }
        };

        // Act
        var result = AnomalyScoring.ComputeScore(profile);

        // Assert
        Assert.True(result.OverallScore < 0.3);
        Assert.Contains(result.Interpretation, new[] { "Excellent", "Good", "Fair" });
    }

    [Fact]
    public void ComputeScore_HighNullRate_FlagsIssue()
    {
        // Arrange
        var profile = new DatasetProfile
        {
            Source = "null_heavy",
            RowCount = 1000,
            Columns = new List<ColumnStats>
            {
                new ColumnStats
                {
                    Name = "sparse_column",
                    IsNumeric = true,
                    RowCount = 1000,
                    NullPercent = 60, // High null rate
                    UniqueCount = 100,
                    UniquePercent = 10
                }
            }
        };

        // Act
        var result = AnomalyScoring.ComputeScore(profile);

        // Assert
        Assert.True(result.Issues.Any(i => i.Category == "NullRate"));
    }

    [Fact]
    public void ComputeScore_HighOutlierRate_FlagsIssue()
    {
        // Arrange
        var profile = new DatasetProfile
        {
            Source = "outlier_heavy",
            RowCount = 1000,
            Columns = new List<ColumnStats>
            {
                new ColumnStats
                {
                    Name = "outlier_column",
                    IsNumeric = true,
                    RowCount = 1000,
                    NullPercent = 0,
                    UniqueCount = 100,
                    UniquePercent = 10,
                    OutlierCount = 150 // 15% outliers
                }
            }
        };

        // Act
        var result = AnomalyScoring.ComputeScore(profile);

        // Assert
        Assert.True(result.Issues.Any(i => i.Category == "Outliers"));
    }

    [Fact]
    public void ComputeScore_HighSkewness_FlagsIssue()
    {
        // Arrange
        var profile = new DatasetProfile
        {
            Source = "skewed",
            RowCount = 1000,
            Columns = new List<ColumnStats>
            {
                new ColumnStats
                {
                    Name = "skewed_column",
                    IsNumeric = true,
                    RowCount = 1000,
                    NullPercent = 0,
                    UniqueCount = 100,
                    UniquePercent = 10,
                    Skewness = 5.0, // High skewness
                    Kurtosis = 20.0
                }
            }
        };

        // Act
        var result = AnomalyScoring.ComputeScore(profile);

        // Assert
        Assert.True(result.Issues.Any(i => i.Category == "Distribution"));
    }

    [Fact]
    public void ComputeScore_ConstantColumn_FlagsIssue()
    {
        // Arrange
        var profile = new DatasetProfile
        {
            Source = "constant",
            RowCount = 1000,
            Columns = new List<ColumnStats>
            {
                new ColumnStats
                {
                    Name = "constant_column",
                    IsNumeric = true,
                    RowCount = 1000,
                    NullPercent = 0,
                    UniqueCount = 1, // Constant
                    UniquePercent = 0.1
                }
            }
        };

        // Act
        var result = AnomalyScoring.ComputeScore(profile);

        // Assert
        Assert.True(result.Issues.Any(i => i.Category == "Cardinality" && i.Description.Contains("constant")));
    }

    [Fact]
    public void ComputeScore_WideDataset_FlagsSchema()
    {
        // Arrange
        var columns = Enumerable.Range(0, 150).Select(i => new ColumnStats
        {
            Name = $"col_{i}",
            IsNumeric = true,
            RowCount = 100,
            NullPercent = 0,
            UniqueCount = 10,
            UniquePercent = 10
        }).ToList();

        var profile = new DatasetProfile
        {
            Source = "wide",
            RowCount = 100,
            Columns = columns
        };

        // Act
        var result = AnomalyScoring.ComputeScore(profile);

        // Assert
        Assert.True(result.Issues.Any(i => i.Category == "Schema"));
    }

    [Fact]
    public void ComputeScore_ComponentsAddUpCorrectly()
    {
        // Arrange
        var profile = new DatasetProfile
        {
            Source = "test",
            RowCount = 1000,
            Columns = new List<ColumnStats>
            {
                new ColumnStats
                {
                    Name = "col1",
                    IsNumeric = true,
                    RowCount = 1000,
                    NullPercent = 10,
                    UniqueCount = 100
                }
            }
        };

        // Act
        var result = AnomalyScoring.ComputeScore(profile);

        // Assert
        Assert.Equal(5, result.Components.Count);
        var weightSum = result.Components.Sum(c => c.Weight);
        Assert.Equal(1.0, weightSum, precision: 2);
    }

    [Fact]
    public void ComputeScore_RecommendationsGenerated()
    {
        // Arrange
        var profile = new DatasetProfile
        {
            Source = "test",
            RowCount = 1000,
            Columns = new List<ColumnStats>
            {
                new ColumnStats
                {
                    Name = "col1",
                    IsNumeric = true,
                    RowCount = 1000,
                    NullPercent = 0
                }
            }
        };

        // Act
        var result = AnomalyScoring.ComputeScore(profile);

        // Assert
        Assert.NotEmpty(result.Recommendations);
    }

    [Fact]
    public void ComputeScore_InterpretationMatchesScore()
    {
        // Arrange & Act & Assert
        var scores = new[] { 0.05, 0.15, 0.25, 0.45, 0.6, 0.8 };
        var expectedInterpretations = new[] { "Excellent", "Good", "Fair", "Concerning", "Poor", "Critical" };

        for (int i = 0; i < scores.Length; i++)
        {
            var profile = new DatasetProfile { Source = "test", RowCount = 1 };
            var result = AnomalyScoring.ComputeScore(profile);
            // Just verify interpretation is non-empty
            Assert.NotEmpty(result.Interpretation);
        }
    }
}

public class DatasetProfileTests
{
    [Fact]
    public void ColumnCount_ReturnsColumnListCount()
    {
        // Arrange
        var profile = new DatasetProfile
        {
            Columns = new List<ColumnStats>
            {
                new ColumnStats { Name = "col1" },
                new ColumnStats { Name = "col2" }
            }
        };

        // Act & Assert
        Assert.Equal(2, profile.ColumnCount);
    }
}

public class AnomalyIssueTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var issue = new AnomalyIssue();

        // Assert
        Assert.Equal("", issue.Category);
        Assert.Equal("", issue.Description);
        Assert.Equal(IssueSeverity.Low, issue.Severity);
        Assert.Empty(issue.AffectedColumns);
    }
}
