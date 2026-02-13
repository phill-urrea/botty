using Microsoft.Extensions.Options;

namespace Botty.Channels.Services;

/// <summary>
/// Interface for splitting long messages into sendable chunks.
/// </summary>
public interface IMessageChunkingService
{
    IList<string> Chunk(string text, int? limitOverride = null, string? modeOverride = null);
}

/// <summary>
/// Splits long messages into chunks at smart boundaries (words, newlines, code blocks).
/// Supports length mode (word/newline boundaries) and newline mode (paragraph boundaries).
/// </summary>
public class MessageChunkingService : IMessageChunkingService
{
    private readonly MessageChunkingOptions _options;

    public MessageChunkingService(IOptions<MessageChunkingOptions> options)
    {
        _options = options.Value;
    }

    public IList<string> Chunk(string text, int? limitOverride = null, string? modeOverride = null)
    {
        var limit = limitOverride ?? _options.ChunkLimit;
        var mode = modeOverride ?? _options.ChunkMode;

        if (string.IsNullOrEmpty(text) || text.Length <= limit)
            return [text];

        return mode.ToLowerInvariant() switch
        {
            "newline" => ChunkByNewline(text, limit),
            _ => ChunkByLength(text, limit)
        };
    }

    /// <summary>
    /// Length mode: smart break at word/newline boundaries, parenthesis-aware.
    /// </summary>
    private static IList<string> ChunkByLength(string text, int limit)
    {
        var chunks = new List<string>();
        var remaining = text;

        while (remaining.Length > limit)
        {
            var breakPoint = FindBreakPoint(remaining, limit);
            var chunk = remaining[..breakPoint].TrimEnd();
            if (chunk.Length > 0)
                chunks.Add(chunk);
            remaining = remaining[breakPoint..].TrimStart();
        }

        if (remaining.Length > 0)
            chunks.Add(remaining);

        return chunks;
    }

    /// <summary>
    /// Finds the best break point within the limit, preferring newlines, then spaces.
    /// Avoids breaking inside code blocks or parenthesized expressions.
    /// </summary>
    private static int FindBreakPoint(string text, int limit)
    {
        // Check if we're inside a code block at the limit
        var codeBlockCount = 0;
        var parenDepth = 0;
        var lastNewline = -1;
        var lastSpace = -1;
        var lastDoubleNewline = -1;

        for (var i = 0; i < limit && i < text.Length; i++)
        {
            var ch = text[i];

            // Track code blocks
            if (i + 2 < text.Length && text[i..(i + 3)] == "```")
            {
                codeBlockCount++;
                i += 2;
                continue;
            }

            // Track parentheses (only outside code blocks)
            if (codeBlockCount % 2 == 0)
            {
                if (ch == '(') parenDepth++;
                else if (ch == ')') parenDepth = Math.Max(0, parenDepth - 1);
            }

            // Track break opportunities
            if (ch == '\n')
            {
                if (i > 0 && text[i - 1] == '\n')
                    lastDoubleNewline = i + 1;
                else if (parenDepth == 0 && codeBlockCount % 2 == 0)
                    lastNewline = i + 1;
            }
            else if (ch == ' ' && parenDepth == 0 && codeBlockCount % 2 == 0)
            {
                lastSpace = i + 1;
            }
        }

        // Prefer double newline (paragraph break), then single newline, then space
        if (lastDoubleNewline > limit / 2) return lastDoubleNewline;
        if (lastNewline > limit / 2) return lastNewline;
        if (lastSpace > limit / 2) return lastSpace;

        // If we're inside a code block, try to find the end of it
        if (codeBlockCount % 2 != 0)
        {
            var codeEnd = text.IndexOf("```", limit, StringComparison.Ordinal);
            if (codeEnd != -1 && codeEnd < limit * 1.5)
                return codeEnd + 3;
        }

        // Fallback: break at limit
        return limit;
    }

    /// <summary>
    /// Newline mode: break on paragraph boundaries (\n\n), code-block safe.
    /// </summary>
    private static IList<string> ChunkByNewline(string text, int limit)
    {
        var chunks = new List<string>();
        var paragraphs = text.Split("\n\n");
        var currentChunk = "";

        foreach (var paragraph in paragraphs)
        {
            var toAdd = string.IsNullOrEmpty(currentChunk) ? paragraph : $"\n\n{paragraph}";

            if (currentChunk.Length + toAdd.Length > limit)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk);
                    currentChunk = "";
                }

                // If a single paragraph exceeds limit, use length-based chunking
                if (paragraph.Length > limit)
                {
                    var subChunks = ChunkByLength(paragraph, limit);
                    for (var i = 0; i < subChunks.Count - 1; i++)
                        chunks.Add(subChunks[i]);
                    currentChunk = subChunks[^1];
                }
                else
                {
                    currentChunk = paragraph;
                }
            }
            else
            {
                currentChunk += toAdd;
            }
        }

        if (currentChunk.Length > 0)
            chunks.Add(currentChunk);

        return chunks;
    }
}
