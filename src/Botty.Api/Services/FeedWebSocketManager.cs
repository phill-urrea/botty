using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Botty.Api.Controllers;
using Microsoft.Extensions.Logging;

namespace Botty.Api.Services;

/// <summary>
/// Manages WebSocket connections for the feed and broadcasts new messages to all clients.
/// </summary>
public class FeedWebSocketManager
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly ILogger<FeedWebSocketManager> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FeedWebSocketManager(ILogger<FeedWebSocketManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Accepts a WebSocket connection and keeps it until closed.
    /// </summary>
    public async Task AcceptConnectionAsync(WebSocket webSocket, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        _sockets.TryAdd(id, webSocket);
        _logger.LogInformation("Feed WebSocket connected: {Id}, total={Count}", id, _sockets.Count);

        try
        {
            var buffer = new byte[1024 * 4];
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Feed WebSocket receive loop ended");
        }
        finally
        {
            _sockets.TryRemove(id, out _);
            if (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
                }
                catch { /* ignore */ }
            }
            webSocket.Dispose();
            _logger.LogInformation("Feed WebSocket disconnected: {Id}, total={Count}", id, _sockets.Count);
        }
    }

    /// <summary>
    /// Broadcasts a new message to all connected clients.
    /// </summary>
    public void BroadcastNewMessage(FeedMessageDto message)
    {
        var payload = JsonSerializer.Serialize(new { type = "new_message", message }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);

        foreach (var kv in _sockets.ToArray())
        {
            if (kv.Value.State == WebSocketState.Open)
            {
                _ = SendAsync(kv.Value, bytes);
            }
        }
    }

    /// <summary>
    /// Broadcasts a typing indicator to all connected clients.
    /// </summary>
    public void BroadcastTypingIndicator(TypingIndicatorDto indicator)
    {
        var payload = JsonSerializer.Serialize(new { type = "typing_indicator", indicator.ConversationId, indicator.Source, indicator.IsTyping, indicator.Timestamp }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);

        foreach (var kv in _sockets.ToArray())
        {
            if (kv.Value.State == WebSocketState.Open)
            {
                _ = SendAsync(kv.Value, bytes);
            }
        }
    }

    /// <summary>
    /// Broadcasts an assistant text delta to all connected clients.
    /// </summary>
    public void BroadcastAssistantDelta(AssistantDeltaDto delta)
    {
        var payload = JsonSerializer.Serialize(new { type = "assistant_delta", delta.ConversationId, delta.MessageId, delta.Delta }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);

        foreach (var kv in _sockets.ToArray())
        {
            if (kv.Value.State == WebSocketState.Open)
            {
                _ = SendAsync(kv.Value, bytes);
            }
        }
    }

    /// <summary>
    /// Broadcasts that an assistant message stream has completed.
    /// </summary>
    public void BroadcastAssistantDone(AssistantDoneDto done)
    {
        var payload = JsonSerializer.Serialize(new { type = "assistant_done", done.ConversationId, done.MessageId, done.Content, done.Usage, done.FinishReason }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);

        foreach (var kv in _sockets.ToArray())
        {
            if (kv.Value.State == WebSocketState.Open)
            {
                _ = SendAsync(kv.Value, bytes);
            }
        }
    }

    private async Task SendAsync(WebSocket webSocket, byte[] bytes)
    {
        try
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send to feed WebSocket client");
        }
    }
}
