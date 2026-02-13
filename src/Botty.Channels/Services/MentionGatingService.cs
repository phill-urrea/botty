namespace Botty.Channels.Services;

/// <summary>
/// Result of mention gating evaluation.
/// </summary>
public record MentionGatingResult(bool ShouldSkip, bool EffectiveWasMentioned);

/// <summary>
/// Determines whether a message should be processed based on mention requirements.
/// </summary>
public static class MentionGatingService
{
    /// <summary>
    /// Resolves whether a message should be skipped based on mention gating rules.
    /// </summary>
    /// <param name="requireMention">Whether the group requires a mention to respond.</param>
    /// <param name="canDetect">Whether the channel can detect mentions.</param>
    /// <param name="wasMentioned">Whether the bot was mentioned in the message.</param>
    /// <param name="isImplicit">Whether this is an implicit mention (e.g., reply to bot's message).</param>
    /// <param name="bypass">Whether to bypass mention gating (e.g., DMs always bypass).</param>
    public static MentionGatingResult Resolve(
        bool requireMention,
        bool canDetect,
        bool wasMentioned,
        bool isImplicit = false,
        bool bypass = false)
    {
        // DMs and bypass always process
        if (bypass)
            return new MentionGatingResult(ShouldSkip: false, EffectiveWasMentioned: true);

        // If mention not required, always process
        if (!requireMention)
            return new MentionGatingResult(ShouldSkip: false, EffectiveWasMentioned: wasMentioned);

        // If we can't detect mentions, process all (fail open)
        if (!canDetect)
            return new MentionGatingResult(ShouldSkip: false, EffectiveWasMentioned: false);

        // Explicit or implicit mention
        if (wasMentioned || isImplicit)
            return new MentionGatingResult(ShouldSkip: false, EffectiveWasMentioned: true);

        // Mention required but not present
        return new MentionGatingResult(ShouldSkip: true, EffectiveWasMentioned: false);
    }
}
