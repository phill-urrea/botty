using Botty.Api.Controllers;

namespace Botty.Api.Services;

/// <summary>
/// No-op implementation of feed broadcast (used until WebSocket is wired).
/// </summary>
public class NoOpFeedBroadcastService : IFeedBroadcastService
{
    /// <inheritdoc />
    public void BroadcastNewMessage(FeedMessageDto message)
    {
        // No-op
    }

    /// <inheritdoc />
    public void BroadcastTypingIndicator(TypingIndicatorDto indicator)
    {
        // No-op
    }

    /// <inheritdoc />
    public void BroadcastAssistantDelta(AssistantDeltaDto delta)
    {
        // No-op
    }

    /// <inheritdoc />
    public void BroadcastAssistantDone(AssistantDoneDto done)
    {
        // No-op
    }
}
