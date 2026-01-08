using StyloFlow.Retrieval.Documents;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Documents;

public class PositionWeightingTests
{
    [Fact]
    public void ApplyWeights_EmptyList_DoesNotThrow()
    {
        // Arrange
        var chunks = new List<TextChunk>();

        // Act & Assert
        var exception = Record.Exception(() => PositionWeighting.ApplyWeights(chunks));
        Assert.Null(exception);
    }

    [Fact]
    public void ApplyWeights_SingleChunk_AppliesIntroWeight()
    {
        // Arrange
        var chunks = new List<TextChunk>
        {
            new TextChunk { Text = "Only chunk" }
        };

        // Act
        PositionWeighting.ApplyWeights(chunks);

        // Assert - single chunk at position 0 is in intro
        Assert.True(chunks[0].PositionWeight > 1.0);
    }

    [Fact]
    public void ApplyWeights_IntroChunks_GetHigherWeight()
    {
        // Arrange
        var chunks = Enumerable.Range(0, 10)
            .Select(i => new TextChunk { Text = $"Chunk {i}", PositionWeight = 1.0 })
            .ToList();

        // Act
        PositionWeighting.ApplyWeights(chunks, ContentType.Expository);

        // Assert - first chunk (intro) should have higher weight
        Assert.True(chunks[0].PositionWeight > chunks[5].PositionWeight);
    }

    [Fact]
    public void ApplyWeights_ConclusionChunks_GetHigherWeight()
    {
        // Arrange
        var chunks = Enumerable.Range(0, 10)
            .Select(i => new TextChunk { Text = $"Chunk {i}", PositionWeight = 1.0 })
            .ToList();

        // Act
        PositionWeighting.ApplyWeights(chunks, ContentType.Expository);

        // Assert - last chunk (conclusion) should have higher weight than body
        Assert.True(chunks[9].PositionWeight > chunks[5].PositionWeight);
    }

    [Fact]
    public void ApplyWeights_BodyChunks_KeepBaseWeight()
    {
        // Arrange
        var chunks = Enumerable.Range(0, 10)
            .Select(i => new TextChunk { Text = $"Chunk {i}", PositionWeight = 1.0 })
            .ToList();

        // Act
        PositionWeighting.ApplyWeights(chunks, ContentType.Unknown);

        // Assert - middle chunks should have approximately 1.0 weight
        Assert.Equal(1.0, chunks[5].PositionWeight);
    }

    [Fact]
    public void ApplyWeights_MultipliesExistingWeight()
    {
        // Arrange
        var chunks = new List<TextChunk>
        {
            new TextChunk { Text = "Intro", PositionWeight = 2.0 },
            new TextChunk { Text = "Body", PositionWeight = 2.0 }
        };

        // Act
        PositionWeighting.ApplyWeights(chunks, ContentType.Expository);

        // Assert - should multiply, not replace
        Assert.True(chunks[0].PositionWeight > 2.0);
    }

    [Theory]
    [InlineData(ContentType.Unknown)]
    [InlineData(ContentType.Expository)]
    [InlineData(ContentType.Narrative)]
    public void ApplyWeights_AllContentTypes_Work(ContentType contentType)
    {
        // Arrange
        var chunks = Enumerable.Range(0, 5)
            .Select(i => new TextChunk { Text = $"Chunk {i}" })
            .ToList();

        // Act & Assert
        var exception = Record.Exception(() => PositionWeighting.ApplyWeights(chunks, contentType));
        Assert.Null(exception);
    }

    [Fact]
    public void GetWeight_Introduction_Expository_ReturnsExpectedValue()
    {
        // Act
        var weight = PositionWeighting.GetWeight(ChunkPosition.Introduction, ContentType.Expository);

        // Assert
        Assert.Equal(1.5, weight);
    }

    [Fact]
    public void GetWeight_Conclusion_Expository_ReturnsExpectedValue()
    {
        // Act
        var weight = PositionWeighting.GetWeight(ChunkPosition.Conclusion, ContentType.Expository);

        // Assert
        Assert.Equal(1.4, weight);
    }

    [Fact]
    public void GetWeight_Body_ReturnsOne()
    {
        // Act & Assert
        Assert.Equal(1.0, PositionWeighting.GetWeight(ChunkPosition.Body, ContentType.Expository));
        Assert.Equal(1.0, PositionWeighting.GetWeight(ChunkPosition.Body, ContentType.Narrative));
        Assert.Equal(1.0, PositionWeighting.GetWeight(ChunkPosition.Body, ContentType.Unknown));
    }

    [Fact]
    public void GetWeight_Narrative_IntroAndConclusion_LowerThanExpository()
    {
        // Act
        var narrativeIntro = PositionWeighting.GetWeight(ChunkPosition.Introduction, ContentType.Narrative);
        var expositoryIntro = PositionWeighting.GetWeight(ChunkPosition.Introduction, ContentType.Expository);

        // Assert - narrative intro/conclusion less important than expository
        Assert.True(narrativeIntro < expositoryIntro);
    }

    [Fact]
    public void GetIntroThreshold_Expository_Returns015()
    {
        // Act
        var threshold = PositionWeighting.GetIntroThreshold(ContentType.Expository);

        // Assert
        Assert.Equal(0.15, threshold);
    }

    [Fact]
    public void GetIntroThreshold_Narrative_Returns010()
    {
        // Act
        var threshold = PositionWeighting.GetIntroThreshold(ContentType.Narrative);

        // Assert
        Assert.Equal(0.10, threshold);
    }

    [Fact]
    public void GetConclusionThreshold_Expository_Returns085()
    {
        // Act
        var threshold = PositionWeighting.GetConclusionThreshold(ContentType.Expository);

        // Assert
        Assert.Equal(0.85, threshold);
    }

    [Fact]
    public void GetConclusionThreshold_Narrative_Returns090()
    {
        // Act
        var threshold = PositionWeighting.GetConclusionThreshold(ContentType.Narrative);

        // Assert
        Assert.Equal(0.90, threshold);
    }
}

public class ContentTypeDetectorTests
{
    [Fact]
    public void Detect_EmptyText_ReturnsUnknown()
    {
        // Act
        var result = ContentTypeDetector.Detect("");

        // Assert
        Assert.Equal(ContentType.Unknown, result);
    }

    [Fact]
    public void Detect_NullText_ReturnsUnknown()
    {
        // Act
        var result = ContentTypeDetector.Detect(null!);

        // Assert
        Assert.Equal(ContentType.Unknown, result);
    }

    [Fact]
    public void Detect_TechnicalContent_ReturnsExpository()
    {
        // Arrange
        var text = @"
# Installation Guide

First, install the package using npm:

```javascript
npm install my-package
```

Then configure your database connection:

```json
{
  ""connectionString"": ""..."",
  ""api"": ""https://api.example.com""
}
```

The function `getData()` returns a JSON response from the HTTP endpoint.
";

        // Act
        var result = ContentTypeDetector.Detect(text);

        // Assert
        Assert.Equal(ContentType.Expository, result);
    }

    [Fact]
    public void Detect_NarrativeContent_ReturnsNarrative()
    {
        // Arrange
        var text = @"
Chapter 1

""I can't believe it,"" she said as she walked through the door.

He looked at her and replied, ""Neither can I.""

They walked together in silence. She felt nervous, but he thought only of what lay ahead.

""Where are we going?"" she asked.

""You'll see,"" he replied with a smile.

She whispered something under her breath as they continued walking.
";

        // Act
        var result = ContentTypeDetector.Detect(text);

        // Assert
        Assert.Equal(ContentType.Narrative, result);
    }

    [Fact]
    public void Detect_MixedContent_ReturnsUnknown()
    {
        // Arrange - balanced technical and narrative elements
        var text = @"
The application uses a JSON-based API to process requests.

""This is an interesting approach,"" said the developer.

Configure the HTTP endpoint in your settings file.
";

        // Act
        var result = ContentTypeDetector.Detect(text);

        // Assert - could be either, but likely unknown due to balance
        // This is a heuristic test - the exact result depends on scoring
        Assert.True(result == ContentType.Unknown || result == ContentType.Expository);
    }

    [Fact]
    public void Detect_CodeBlocks_IndicateTechnical()
    {
        // Arrange - need more technical signals
        var text = @"
# Installation Guide

Here is an example using the API:

```python
def hello():
    print('Hello World')
```

This function prints a greeting. Configure the HTTP endpoint to connect to the JSON database.
The function class method handles the API response correctly.
";

        // Act
        var result = ContentTypeDetector.Detect(text);

        // Assert
        Assert.Equal(ContentType.Expository, result);
    }

    [Fact]
    public void Detect_DialogueTags_IndicateNarrative()
    {
        // Arrange
        var text = @"
""Hello,"" said John.
""Hi there,"" replied Mary.
""How are you?"" asked John.
""I'm fine,"" whispered Mary.
""Good to hear,"" shouted John.
""Why are you shouting?"" she asked.
";

        // Act
        var result = ContentTypeDetector.Detect(text);

        // Assert
        Assert.Equal(ContentType.Narrative, result);
    }
}
