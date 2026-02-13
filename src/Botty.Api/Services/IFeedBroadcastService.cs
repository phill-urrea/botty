using Botty.Api.Controllers;

namespace Botty.Api.Services;

/// <summary>
/// Broadcasts new feed messages to connected clients (e.g. via WebSocket).
/// </summary>
public interface IFeedBroadcastService
{
    /// <summary>
    /// Notifies all subscribers that a new message was added to the feed.
    /// </summary>
    void BroadcastNewMessage(FeedMessageDto message);

    /// <summary>
    /// Notifies all subscribers of a typing indicator state change.
    /// </summary>
    void BroadcastTypingIndicator(TypingIndicatorDto indicator);

    /// <summary>
    /// Broadcasts a streaming text delta for an assistant message.
    /// </summary>
    void BroadcastAssistantDelta(AssistantDeltaDto delta);

    /// <summary>
    /// Broadcasts that an assistant message stream has completed.
    /// </summary>
    void BroadcastAssistantDone(AssistantDoneDto done);
}
