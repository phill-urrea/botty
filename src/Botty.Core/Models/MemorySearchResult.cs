namespace Botty.Core.Models;

/// <summary>
/// A memory search result with scoring information from vector and/or full-text search.
/// </summary>
public class MemorySearchResult
{
    /// <summary>
    /// The matched memory.
    /// </summary>
    public required Memory Memory { get; set; }

    /// <summary>
    /// Vector similarity score (0-1, higher is more similar). Computed as 1 - cosine_distance.
    /// </summary>
    public double VectorScore { get; set; }

    /// <summary>
    /// Full-text search rank score (0-1, higher is more relevant).
    /// </summary>
    public double TextScore { get; set; }

    /// <summary>
    /// Combined hybrid score: vectorWeight * VectorScore + textWeight * TextScore.
    /// </summary>
    public double HybridScore { get; set; }
}
