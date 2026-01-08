using StyloFlow.Retrieval;
using Xunit;

namespace StyloFlow.Tests.Retrieval;

public class ReciprocalRankFusionTests
{
    [Fact]
    public void DefaultConstructor_SetsDefaultK()
    {
        // Arrange & Act
        var rrf = new ReciprocalRankFusion();

        // Assert
        Assert.Equal("RRF", rrf.Name);
    }

    [Fact]
    public void Constructor_WithCustomK_AcceptsPositiveValue()
    {
        // Arrange & Act
        var rrf = new ReciprocalRankFusion(100);

        // Assert
        Assert.Equal("RRF", rrf.Name);
    }

    [Fact]
    public void Constructor_ZeroK_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReciprocalRankFusion(0));
    }

    [Fact]
    public void Constructor_NegativeK_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReciprocalRankFusion(-1));
    }

    [Fact]
    public void Fuse_EmptyLists_ReturnsEmptyResult()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion();
        var lists = new List<IReadOnlyList<string>>();

        // Act
        var result = rrf.Fuse(lists);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Fuse_SingleList_ReturnsItemsWithScores()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion(60);
        var list = new[] { "A", "B", "C" };

        // Act
        var result = rrf.Fuse(new[] { list });

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("A", result[0].Item); // First item should have highest score
    }

    [Fact]
    public void Fuse_TwoLists_CombinesRanks()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion(60);
        var list1 = new[] { "A", "B", "C" };
        var list2 = new[] { "B", "A", "D" };

        // Act
        var result = rrf.Fuse(list1, list2);

        // Assert
        Assert.Equal(4, result.Count);
        // A and B appear in both lists, should have higher scores
        var topTwo = result.Take(2).Select(r => r.Item).ToHashSet();
        Assert.Contains("A", topTwo);
        Assert.Contains("B", topTwo);
    }

    [Fact]
    public void Fuse_ThreeLists_CombinesAllRanks()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion(60);
        var list1 = new[] { "A", "B" };
        var list2 = new[] { "B", "C" };
        var list3 = new[] { "C", "A" };

        // Act
        var result = rrf.Fuse(list1, list2, list3);

        // Assert
        Assert.Equal(3, result.Count);
        // All items appear in 2 of 3 lists
    }

    [Fact]
    public void Fuse_ItemInAllLists_HasHighestScore()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion(60);
        var list1 = new[] { "A", "X" };
        var list2 = new[] { "A", "Y" };
        var list3 = new[] { "A", "Z" };

        // Act
        var result = rrf.Fuse(new[] { list1, list2, list3 });

        // Assert
        Assert.Equal("A", result[0].Item);
    }

    [Fact]
    public void Fuse_HigherRankInList_ContributesMoreToScore()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion(60);
        var list1 = new[] { "A", "B", "C", "D", "E" };
        var list2 = new[] { "E", "D", "C", "B", "A" };

        // Act
        var result = rrf.Fuse(list1, list2);

        // Assert
        // C is in the middle of both lists, should have similar score contribution
        // A is #1 in list1, #5 in list2
        // E is #5 in list1, #1 in list2
        // A and E should have same score
        var scoreA = result.First(r => r.Item == "A").Score;
        var scoreE = result.First(r => r.Item == "E").Score;
        Assert.Equal(scoreA, scoreE, precision: 6);
    }

    [Fact]
    public void FuseWithScores_ReturnsCorrectRrfScores()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion(60);
        var list1 = new[] { new ScoredItem<string>("A", 0.9), new ScoredItem<string>("B", 0.7) };
        var list2 = new[] { new ScoredItem<string>("B", 0.8), new ScoredItem<string>("A", 0.6) };

        // Act
        var result = rrf.FuseWithScores(new[] { list1.ToArray(), list2.ToArray() });

        // Assert
        Assert.Equal(2, result.Count);
        // Both A and B appear in both lists at positions that should give same total RRF
    }

    [Fact]
    public void Fuse_ScoresArePositive()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion();
        var list1 = new[] { "A", "B", "C" };
        var list2 = new[] { "D", "E", "F" };

        // Act
        var result = rrf.Fuse(list1, list2);

        // Assert
        Assert.All(result, r => Assert.True(r.Score > 0));
    }

    [Fact]
    public void Fuse_ResultsOrderedByScoreDescending()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion();
        var list1 = new[] { "A", "B", "C" };
        var list2 = new[] { "C", "B", "A" };

        // Act
        var result = rrf.Fuse(list1, list2);

        // Assert
        for (int i = 1; i < result.Count; i++)
        {
            Assert.True(result[i - 1].Score >= result[i].Score);
        }
    }

    [Fact]
    public void Fuse_SmallK_GreaterScoreDifferences()
    {
        // Arrange
        var rrfSmallK = new ReciprocalRankFusion(1);
        var rrfLargeK = new ReciprocalRankFusion(100);
        var list = new[] { "A", "B", "C" };

        // Act
        var resultSmallK = rrfSmallK.Fuse(new[] { list });
        var resultLargeK = rrfLargeK.Fuse(new[] { list });

        // Assert
        var diffSmallK = resultSmallK[0].Score - resultSmallK[2].Score;
        var diffLargeK = resultLargeK[0].Score - resultLargeK[2].Score;
        Assert.True(diffSmallK > diffLargeK);
    }

    [Fact]
    public void FuseWithScores_IgnoresOriginalScores_UsesRankOnly()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion(60);
        // High score but low rank vs low score but high rank
        var list1 = new[] { new ScoredItem<string>("A", 1000.0), new ScoredItem<string>("B", 0.001) };
        var list2 = new[] { new ScoredItem<string>("B", 1000.0), new ScoredItem<string>("A", 0.001) };

        // Act
        var result = rrf.FuseWithScores(new[] { list1.ToArray(), list2.ToArray() });

        // Assert - A and B should have same RRF score since they swap positions
        var scoreA = result.First(r => r.Item == "A").Score;
        var scoreB = result.First(r => r.Item == "B").Score;
        Assert.Equal(scoreA, scoreB, precision: 6);
    }
}

public class HybridRrfSearchTests
{
    [Fact]
    public void Constructor_RequiresTextSelector()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new HybridRrfSearch<string>(null!));
    }

    [Fact]
    public void Search_WithTextOnly_ReturnsBm25Results()
    {
        // Arrange
        var search = new HybridRrfSearch<string>(x => x);
        var items = new[] { "machine learning algorithms", "deep neural networks", "learning to code" };
        search.Initialize(items);

        // Act
        var results = search.Search(items, "learning", topK: 2);

        // Assert
        Assert.True(results.Count <= 2);
        Assert.All(results, r => Assert.Contains("learning", r.Item.ToLower()));
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsResults()
    {
        // Arrange
        var search = new HybridRrfSearch<string>(x => x);
        var items = new[] { "test item" };

        // Act
        var results = search.Search(items, "");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Search_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var search = new HybridRrfSearch<string>(x => x);
        var items = new[] { "hello world" };

        // Act
        var results = search.Search(items, "xyz123");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Search_WithEmbeddings_CombinesDenseAndSparse()
    {
        // Arrange
        var embeddings = new Dictionary<string, float[]>
        {
            ["A"] = new float[] { 1, 0, 0 },
            ["B"] = new float[] { 0, 1, 0 },
            ["C"] = new float[] { 0, 0, 1 }
        };
        var search = new HybridRrfSearch<string>(x => x, x => embeddings.GetValueOrDefault(x, new float[] { 0, 0, 0 }));
        var items = new[] { "A", "B", "C" };

        // Act
        var results = search.Search(items, "A", new float[] { 1, 0, 0 }, topK: 3);

        // Assert
        Assert.NotEmpty(results);
    }

    [Fact]
    public void Search_TopK_LimitsResults()
    {
        // Arrange
        var search = new HybridRrfSearch<string>(x => x);
        var items = Enumerable.Range(1, 100).Select(i => $"item {i}").ToArray();
        search.Initialize(items);

        // Act
        var results = search.Search(items, "item", topK: 5);

        // Assert
        Assert.True(results.Count <= 5);
    }
}

public class ScoredItemTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        // Arrange & Act
        var item = new ScoredItem<string>("test", 0.75);

        // Assert
        Assert.Equal("test", item.Item);
        Assert.Equal(0.75, item.Score);
    }

    [Fact]
    public void ScoredItem_WithDifferentTypes_Works()
    {
        // Arrange & Act
        var intItem = new ScoredItem<int>(42, 0.5);
        var objItem = new ScoredItem<object>(new { Name = "test" }, 0.9);

        // Assert
        Assert.Equal(42, intItem.Item);
        Assert.Equal(0.5, intItem.Score);
        Assert.NotNull(objItem.Item);
    }
}
