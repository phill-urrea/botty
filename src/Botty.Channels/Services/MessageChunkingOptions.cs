namespace Botty.Channels.Services;

/// <summary>
/// Configuration for message chunking behavior.
/// </summary>
public class MessageChunkingOptions
{
    /// <summary>
    /// Maximum characters per chunk. Default: 4000.
    /// </summary>
    public int ChunkLimit { get; set; } = 4000;

    /// <summary>
    /// Chunking mode: "length" breaks at word/newline boundaries, "newline" breaks at paragraph boundaries.
    /// </summary>
    public string ChunkMode { get; set; } = "length";
}
