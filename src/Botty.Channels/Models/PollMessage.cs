namespace Botty.Channels;

/// <summary>
/// Input data for creating a poll.
/// </summary>
public class PollInput
{
    /// <summary>
    /// The poll question.
    /// </summary>
    public required string Question { get; set; }

    /// <summary>
    /// List of answer options (2-12 items).
    /// </summary>
    public required IList<string> Options { get; set; }

    /// <summary>
    /// Maximum number of selections allowed. Default: 1.
    /// </summary>
    public int MaxSelections { get; set; } = 1;

    /// <summary>
    /// Duration in hours before the poll closes. 0 = no expiry.
    /// </summary>
    public int DurationHours { get; set; } = 0;
}

/// <summary>
/// Outbound poll message to send through a channel.
/// </summary>
public record OutboundPollMessage(
    string ChatId,
    PollInput Poll,
    string? ReplyToMessageId = null
);
