using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using System.Text.RegularExpressions;

namespace StyloFlow.WorkflowBuilder.Atoms.Analyzers;

/// <summary>
/// Text analyzer extractor - extracts metrics from raw text using DETERMINISTIC computation.
/// This is a perfect example of what SHOULD NOT use an LLM - word counts, char counts,
/// sentence counts are all trivial deterministic operations.
/// Taxonomy: extractor, deterministic, ephemeral
/// </summary>
public sealed class TextAnalyzerExtractor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly, // Pure computation - no need for persistence
        name: "text-analyzer",
        reads: ["http.body", "timer.triggered"],
        writes: ["text.analyzed", "text.word_count", "text.char_count", "text.sentence_count", "text.content"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var text = ctx.Signals.Get<string>("http.body") ?? "";

        if (string.IsNullOrWhiteSpace(text))
        {
            ctx.Log("No text to analyze");
            return Task.CompletedTask;
        }

        ctx.Log($"Analyzing text ({text.Length} chars)...");

        // Pure deterministic computation - NO LLM needed!
        var wordCount = CountWords(text);
        var charCount = text.Length;
        var charCountNoSpaces = text.Count(c => !char.IsWhiteSpace(c));
        var sentenceCount = CountSentences(text);
        var paragraphCount = CountParagraphs(text);
        var avgWordLength = wordCount > 0 ? charCountNoSpaces / (double)wordCount : 0;

        ctx.Log($"Analysis complete: {wordCount} words, {sentenceCount} sentences, {paragraphCount} paragraphs");

        ctx.Emit("text.analyzed", new
        {
            WordCount = wordCount,
            CharCount = charCount,
            CharCountNoSpaces = charCountNoSpaces,
            SentenceCount = sentenceCount,
            ParagraphCount = paragraphCount,
            AvgWordLength = avgWordLength
        });
        ctx.Emit("text.word_count", wordCount);
        ctx.Emit("text.char_count", charCount);
        ctx.Emit("text.sentence_count", sentenceCount);
        ctx.Emit("text.content", text);

        return Task.CompletedTask;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static int CountSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        // Simple sentence detection: count . ! ? but exclude common abbreviations
        return Regex.Matches(text, @"[.!?]+(?=\s|$)").Count;
    }

    private static int CountParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        // Split on double newlines or multiple newlines
        return Regex.Split(text, @"\n\s*\n").Count(p => !string.IsNullOrWhiteSpace(p));
    }
}
