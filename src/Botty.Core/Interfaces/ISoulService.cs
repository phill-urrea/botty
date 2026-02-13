using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Service interface for managing the Soul.md configuration.
/// </summary>
public interface ISoulService
{
    /// <summary>
    /// Gets the current active Soul configuration.
    /// </summary>
    Task<SoulConfiguration> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates the Soul configuration with new content.
    /// </summary>
    /// <param name="markdown">The new markdown content.</param>
    /// <param name="changedBy">Who made the change.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SoulConfiguration> UpdateAsync(string markdown, string changedBy, CancellationToken ct = default);

    /// <summary>
    /// Gets the version history of Soul configurations.
    /// </summary>
    Task<IEnumerable<SoulVersion>> GetHistoryAsync(CancellationToken ct = default);

    /// <summary>
    /// Reverts to a previous version.
    /// </summary>
    Task<SoulConfiguration> RevertToVersionAsync(Guid versionId, CancellationToken ct = default);

    /// <summary>
    /// Generates the system prompt from Soul configuration and memory pack.
    /// </summary>
    /// <param name="soul">The Soul configuration.</param>
    /// <param name="memoryPack">The memory pack to include.</param>
    string GenerateSystemPrompt(SoulConfiguration soul, string memoryPack);

    /// <summary>
    /// Parses raw markdown into a SoulConfiguration.
    /// </summary>
    SoulConfiguration ParseMarkdown(string markdown);
}
