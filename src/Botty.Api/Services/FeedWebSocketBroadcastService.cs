using Botty.Api.Controllers;

namespace Botty.Api.Services;

/// <summary>
/// Broadcasts new feed messages to connected WebSocket clients.
/// </summary>
public class FeedWebSocketBroadcastService : IFeedBroadcastService
{
    private readonly FeedWebSocketManager _manager;

    public FeedWebSocketBroadcastService(FeedWebSocketManager manager)
    {
        _manager = manager;
    }

    /// <inheritdoc />
    public void BroadcastNewMessage(FeedMessageDto message)
    {
        _manager.BroadcastNewMessage(message);
    }

    /// <inheritdoc />
    public void BroadcastTypingIndicator(TypingIndicatorDto indicator)
    {
        _manager.BroadcastTypingIndicator(indicator);
    }
}
