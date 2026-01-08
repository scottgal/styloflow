using System.Text.RegularExpressions;

namespace StyloFlow.Retrieval.Documents;

/// <summary>
/// Document chunking strategies for RAG and retrieval.
/// Splits documents into semantically meaningful chunks.
/// Uses source-generated regex patterns for optimal performance.
/// </summary>
public static partial class DocumentChunker
{
    #region Source-Generated Regex Patterns

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z])|(?<=[.!?])$")]
    private static partial Regex SentenceBoundaryRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex RecursiveSentenceSplitRegex();

    #endregion
    /// <summary>
    /// Chunk text using sliding window with overlap.
    /// Good for: Dense retrieval where context continuity matters.
    /// </summary>
    public static IEnumerable<TextChunk> SlidingWindow(
        string text,
        int windowSize = 512,
        int overlap = 128)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var step = windowSize - overlap;
        var chunkIndex = 0;

        for (var i = 0; i < words.Length; i += step)
        {
            var chunkWords = words.Skip(i).Take(windowSize).ToArray();
            if (chunkWords.Length == 0) break;

            yield return new TextChunk
            {
                Index = chunkIndex++,
                Text = string.Join(' ', chunkWords),
                StartOffset = i,
                EndOffset = i + chunkWords.Length - 1
            };

            if (i + windowSize >= words.Length) break;
        }
    }

    /// <summary>
    /// Chunk text by sentence boundaries.
    /// Good for: Precise retrieval, Q&A, summarization.
    /// </summary>
    public static IEnumerable<TextChunk> BySentence(
        string text,
        int minChunkSize = 100,
        int maxChunkSize = 1000)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var sentences = SentenceBoundaryRegex().Split(text)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        var currentChunk = new List<string>();
        var currentLength = 0;
        var chunkIndex = 0;
        var startOffset = 0;

        foreach (var sentence in sentences)
        {
            if (currentLength + sentence.Length > maxChunkSize && currentChunk.Count > 0)
            {
                yield return new TextChunk
                {
                    Index = chunkIndex++,
                    Text = string.Join(' ', currentChunk),
                    StartOffset = startOffset,
                    EndOffset = startOffset + currentLength
                };

                startOffset += currentLength;
                currentChunk.Clear();
                currentLength = 0;
            }

            currentChunk.Add(sentence);
            currentLength += sentence.Length + 1;
        }

        if (currentChunk.Count > 0 && currentLength >= minChunkSize)
        {
            yield return new TextChunk
            {
                Index = chunkIndex,
                Text = string.Join(' ', currentChunk),
                StartOffset = startOffset,
                EndOffset = startOffset + currentLength
            };
        }
    }

    /// <summary>
    /// Chunk text by paragraph boundaries.
    /// Good for: Long-form documents, articles, books.
    /// </summary>
    public static IEnumerable<TextChunk> ByParagraph(
        string text,
        int minChunkSize = 100,
        int maxChunkSize = 2000)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        var currentChunk = new List<string>();
        var currentLength = 0;
        var chunkIndex = 0;
        var startOffset = 0;

        foreach (var paragraph in paragraphs)
        {
            if (currentLength + paragraph.Length > maxChunkSize && currentChunk.Count > 0)
            {
                yield return new TextChunk
                {
                    Index = chunkIndex++,
                    Text = string.Join("\n\n", currentChunk),
                    StartOffset = startOffset,
                    EndOffset = startOffset + currentLength
                };

                startOffset += currentLength;
                currentChunk.Clear();
                currentLength = 0;
            }

            currentChunk.Add(paragraph);
            currentLength += paragraph.Length + 2;
        }

        if (currentChunk.Count > 0 && currentLength >= minChunkSize)
        {
            yield return new TextChunk
            {
                Index = chunkIndex,
                Text = string.Join("\n\n", currentChunk),
                StartOffset = startOffset,
                EndOffset = startOffset + currentLength
            };
        }
    }

    /// <summary>
    /// Chunk markdown by heading sections.
    /// Good for: Documentation, technical docs, structured content.
    /// </summary>
    public static IEnumerable<TextChunk> ByMarkdownSection(
        string markdown,
        int maxLevel = 2,
        int maxChunkSize = 2000)
    {
        if (string.IsNullOrEmpty(markdown)) yield break;

        var headingPattern = new Regex(
            $@"^(#{{{1},{maxLevel}}})\s+(.+)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        var matches = headingPattern.Matches(markdown).ToList();

        if (matches.Count == 0)
        {
            // No headings - return as single chunk or split by paragraph
            foreach (var chunk in ByParagraph(markdown, maxChunkSize: maxChunkSize))
                yield return chunk;
            yield break;
        }

        var chunkIndex = 0;

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var startIdx = match.Index;
            var endIdx = i + 1 < matches.Count ? matches[i + 1].Index : markdown.Length;

            var sectionText = markdown[startIdx..endIdx].Trim();
            var headingLevel = match.Groups[1].Value.Length;
            var headingText = match.Groups[2].Value;

            // If section is too large, split by paragraph
            if (sectionText.Length > maxChunkSize)
            {
                foreach (var subChunk in ByParagraph(sectionText, maxChunkSize: maxChunkSize))
                {
                    subChunk.Index = chunkIndex++;
                    subChunk.Metadata["heading"] = headingText;
                    subChunk.Metadata["heading_level"] = headingLevel;
                    yield return subChunk;
                }
            }
            else
            {
                yield return new TextChunk
                {
                    Index = chunkIndex++,
                    Text = sectionText,
                    StartOffset = startIdx,
                    EndOffset = endIdx,
                    Metadata = new Dictionary<string, object>
                    {
                        ["heading"] = headingText,
                        ["heading_level"] = headingLevel
                    }
                };
            }
        }
    }

    /// <summary>
    /// Recursive chunking that respects semantic boundaries.
    /// Tries paragraph → sentence → character splits progressively.
    /// </summary>
    public static IEnumerable<TextChunk> Recursive(
        string text,
        int targetSize = 500,
        int minSize = 100,
        int maxSize = 1000)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        if (text.Length <= maxSize)
        {
            yield return new TextChunk { Index = 0, Text = text };
            yield break;
        }

        // Try paragraph split first
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (paragraphs.Length > 1)
        {
            var chunkIndex = 0;
            foreach (var para in paragraphs)
            {
                foreach (var chunk in Recursive(para, targetSize, minSize, maxSize))
                {
                    chunk.Index = chunkIndex++;
                    yield return chunk;
                }
            }
            yield break;
        }

        // Try sentence split
        var sentences = RecursiveSentenceSplitRegex().Split(text);
        if (sentences.Length > 1)
        {
            var currentChunk = new List<string>();
            var currentLength = 0;
            var chunkIndex = 0;

            foreach (var sentence in sentences)
            {
                if (currentLength + sentence.Length > targetSize && currentChunk.Count > 0)
                {
                    var chunkText = string.Join(" ", currentChunk);
                    if (chunkText.Length >= minSize)
                    {
                        yield return new TextChunk { Index = chunkIndex++, Text = chunkText };
                    }
                    currentChunk.Clear();
                    currentLength = 0;
                }

                currentChunk.Add(sentence);
                currentLength += sentence.Length + 1;
            }

            if (currentChunk.Count > 0)
            {
                var finalText = string.Join(" ", currentChunk);
                if (finalText.Length >= minSize)
                {
                    yield return new TextChunk { Index = chunkIndex, Text = finalText };
                }
            }
            yield break;
        }

        // Fall back to character split
        foreach (var chunk in SlidingWindow(text, targetSize, targetSize / 4))
            yield return chunk;
    }
}

/// <summary>
/// Represents a chunk of text with metadata.
/// </summary>
public class TextChunk
{
    public int Index { get; set; }
    public required string Text { get; init; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Embedding for this chunk (populated during retrieval).
    /// </summary>
    public float[]? Embedding { get; set; }

    /// <summary>
    /// Salience/relevance score.
    /// </summary>
    public double SalienceScore { get; set; }

    /// <summary>
    /// Position weight (intro/conclusion boost).
    /// </summary>
    public double PositionWeight { get; set; } = 1.0;
}
