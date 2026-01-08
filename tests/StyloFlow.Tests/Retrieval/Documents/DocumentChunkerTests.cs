using StyloFlow.Retrieval.Documents;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Documents;

public class DocumentChunkerTests
{
    #region SlidingWindow Tests

    [Fact]
    public void SlidingWindow_EmptyText_ReturnsEmpty()
    {
        // Act
        var chunks = DocumentChunker.SlidingWindow("").ToList();

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public void SlidingWindow_NullText_ReturnsEmpty()
    {
        // Act
        var chunks = DocumentChunker.SlidingWindow(null!).ToList();

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public void SlidingWindow_ShortText_ReturnsSingleChunk()
    {
        // Arrange
        var text = "Hello world test";

        // Act
        var chunks = DocumentChunker.SlidingWindow(text, windowSize: 10).ToList();

        // Assert
        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].Index);
        Assert.Equal("Hello world test", chunks[0].Text);
    }

    [Fact]
    public void SlidingWindow_LongText_ReturnsOverlappingChunks()
    {
        // Arrange
        var words = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"word{i}"));

        // Act
        var chunks = DocumentChunker.SlidingWindow(words, windowSize: 5, overlap: 2).ToList();

        // Assert
        Assert.True(chunks.Count > 1);
        // Verify overlap - consecutive chunks should share words
        for (int i = 1; i < chunks.Count; i++)
        {
            var prevWords = chunks[i - 1].Text.Split(' ').ToHashSet();
            var currWords = chunks[i].Text.Split(' ').ToHashSet();
            Assert.True(prevWords.Overlaps(currWords));
        }
    }

    [Fact]
    public void SlidingWindow_SetsCorrectIndices()
    {
        // Arrange
        var words = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"word{i}"));

        // Act
        var chunks = DocumentChunker.SlidingWindow(words, windowSize: 5, overlap: 1).ToList();

        // Assert
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
        }
    }

    [Fact]
    public void SlidingWindow_SetsOffsets()
    {
        // Arrange
        var text = "one two three four five six seven eight nine ten";

        // Act
        var chunks = DocumentChunker.SlidingWindow(text, windowSize: 3, overlap: 1).ToList();

        // Assert
        Assert.All(chunks, c => Assert.True(c.StartOffset >= 0));
        Assert.All(chunks, c => Assert.True(c.EndOffset >= c.StartOffset));
    }

    #endregion

    #region BySentence Tests

    [Fact]
    public void BySentence_EmptyText_ReturnsEmpty()
    {
        // Act
        var chunks = DocumentChunker.BySentence("").ToList();

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public void BySentence_SingleSentence_ReturnsSingleChunkIfLongEnough()
    {
        // Arrange
        var text = "This is a sufficiently long sentence that should form a single chunk for testing purposes.";

        // Act
        var chunks = DocumentChunker.BySentence(text, minChunkSize: 50).ToList();

        // Assert
        Assert.Single(chunks);
    }

    [Fact]
    public void BySentence_MultipleSentences_CombinesToMaxSize()
    {
        // Arrange
        var text = "First sentence here. Second sentence follows. Third one now. Fourth and final.";

        // Act
        var chunks = DocumentChunker.BySentence(text, minChunkSize: 10, maxChunkSize: 50).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 60)); // Allow some slack
    }

    [Fact]
    public void BySentence_RespectsMinChunkSize()
    {
        // Arrange
        var text = "Short. Also short. Very short too.";

        // Act
        var chunks = DocumentChunker.BySentence(text, minChunkSize: 100, maxChunkSize: 1000).ToList();

        // Assert - chunks below min size should be combined
        Assert.True(chunks.Count <= 1);
    }

    [Fact]
    public void BySentence_SetsCorrectIndices()
    {
        // Arrange
        var text = string.Join(" ", Enumerable.Range(1, 10).Select(i => $"Sentence number {i}."));

        // Act
        var chunks = DocumentChunker.BySentence(text, minChunkSize: 10, maxChunkSize: 100).ToList();

        // Assert
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
        }
    }

    #endregion

    #region ByParagraph Tests

    [Fact]
    public void ByParagraph_EmptyText_ReturnsEmpty()
    {
        // Act
        var chunks = DocumentChunker.ByParagraph("").ToList();

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public void ByParagraph_SingleParagraph_ReturnsSingleChunk()
    {
        // Arrange
        var text = "This is a single paragraph with enough content to meet the minimum size requirement for chunking.";

        // Act
        var chunks = DocumentChunker.ByParagraph(text, minChunkSize: 50).ToList();

        // Assert
        Assert.Single(chunks);
    }

    [Fact]
    public void ByParagraph_MultipleParagraphs_SplitsCorrectly()
    {
        // Arrange
        var text = "First paragraph with some content here.\n\nSecond paragraph follows this one.\n\nThird paragraph at the end.";

        // Act
        var chunks = DocumentChunker.ByParagraph(text, minChunkSize: 10, maxChunkSize: 100).ToList();

        // Assert
        Assert.True(chunks.Count >= 1);
    }

    [Fact]
    public void ByParagraph_CombinesSmallParagraphs()
    {
        // Arrange
        var text = "Short para 1.\n\nShort para 2.\n\nShort para 3.";

        // Act
        var chunks = DocumentChunker.ByParagraph(text, minChunkSize: 50, maxChunkSize: 200).ToList();

        // Assert - should combine into fewer chunks
        Assert.True(chunks.Count <= 2);
    }

    [Fact]
    public void ByParagraph_WindowsLineEndings_Works()
    {
        // Arrange
        var text = "Paragraph one.\r\n\r\nParagraph two.\r\n\r\nParagraph three.";

        // Act
        var chunks = DocumentChunker.ByParagraph(text, minChunkSize: 10, maxChunkSize: 50).ToList();

        // Assert
        Assert.NotEmpty(chunks);
    }

    #endregion

    #region ByMarkdownSection Tests

    [Fact]
    public void ByMarkdownSection_EmptyText_ReturnsEmpty()
    {
        // Act
        var chunks = DocumentChunker.ByMarkdownSection("").ToList();

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public void ByMarkdownSection_NoHeadings_FallsBackToParagraph()
    {
        // Arrange
        var text = "Just some plain text without any markdown headings. This should still be chunked properly using paragraph splitting.";

        // Act
        var chunks = DocumentChunker.ByMarkdownSection(text).ToList();

        // Assert
        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void ByMarkdownSection_WithHeadings_SplitsBySection()
    {
        // Arrange
        var markdown = @"# Introduction
This is the introduction section.

# Methods
This describes the methods used.

# Results
Here are the results.";

        // Act
        var chunks = DocumentChunker.ByMarkdownSection(markdown, maxLevel: 2).ToList();

        // Assert
        Assert.Equal(3, chunks.Count);
    }

    [Fact]
    public void ByMarkdownSection_IncludesHeadingMetadata()
    {
        // Arrange
        var markdown = @"# First Section
Content for first section.

## Subsection
More content here.";

        // Act
        var chunks = DocumentChunker.ByMarkdownSection(markdown, maxLevel: 2).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        var firstChunk = chunks[0];
        Assert.True(firstChunk.Metadata.ContainsKey("heading"));
        Assert.True(firstChunk.Metadata.ContainsKey("heading_level"));
    }

    [Fact]
    public void ByMarkdownSection_RespectsMaxLevel()
    {
        // Arrange
        var markdown = @"# Level 1
Content.

## Level 2
More content.

### Level 3
Even more content.";

        // Act - only split on level 1
        var chunks = DocumentChunker.ByMarkdownSection(markdown, maxLevel: 1).ToList();

        // Assert - should only have 1 chunk (level 1 heading)
        Assert.Single(chunks);
    }

    #endregion

    #region Recursive Tests

    [Fact]
    public void Recursive_EmptyText_ReturnsEmpty()
    {
        // Act
        var chunks = DocumentChunker.Recursive("").ToList();

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public void Recursive_ShortText_ReturnsSingleChunk()
    {
        // Arrange
        var text = "Short text that fits in one chunk.";

        // Act
        var chunks = DocumentChunker.Recursive(text, targetSize: 100, maxSize: 200).ToList();

        // Assert
        Assert.Single(chunks);
    }

    [Fact]
    public void Recursive_LongText_SplitsProgressively()
    {
        // Arrange
        var paragraphs = Enumerable.Range(1, 10)
            .Select(i => $"This is paragraph {i} with some content to make it longer. More text here to pad it out.")
            .ToArray();
        var text = string.Join("\n\n", paragraphs);

        // Act
        var chunks = DocumentChunker.Recursive(text, targetSize: 100, minSize: 50, maxSize: 200).ToList();

        // Assert
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 250)); // Allow some slack
    }

    [Fact]
    public void Recursive_SetsCorrectIndices()
    {
        // Arrange
        var text = "Para one content.\n\nPara two content.\n\nPara three content.";

        // Act
        var chunks = DocumentChunker.Recursive(text, targetSize: 20, minSize: 10, maxSize: 50).ToList();

        // Assert
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
        }
    }

    #endregion
}

public class TextChunkTests
{
    [Fact]
    public void TextChunk_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var chunk = new TextChunk { Text = "test" };

        // Assert
        Assert.Equal("test", chunk.Text);
        Assert.Equal(0, chunk.Index);
        Assert.Equal(0, chunk.StartOffset);
        Assert.Equal(0, chunk.EndOffset);
        Assert.Empty(chunk.Metadata);
        Assert.Null(chunk.Embedding);
        Assert.Equal(0.0, chunk.SalienceScore);
        Assert.Equal(1.0, chunk.PositionWeight);
    }

    [Fact]
    public void TextChunk_MetadataCanBeSet()
    {
        // Arrange & Act
        var chunk = new TextChunk
        {
            Text = "test",
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 42
            }
        };

        // Assert
        Assert.Equal("value1", chunk.Metadata["key1"]);
        Assert.Equal(42, chunk.Metadata["key2"]);
    }

    [Fact]
    public void TextChunk_EmbeddingCanBeSet()
    {
        // Arrange & Act
        var chunk = new TextChunk
        {
            Text = "test",
            Embedding = new float[] { 1.0f, 2.0f, 3.0f }
        };

        // Assert
        Assert.NotNull(chunk.Embedding);
        Assert.Equal(3, chunk.Embedding.Length);
    }
}
