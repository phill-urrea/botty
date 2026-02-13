using System.Text.Json;

namespace Botty.Hooks.Models;

/// <summary>
/// Context passed to hook execution with trigger and payload.
/// </summary>
public class HookContext
{
    public required HookTrigger Trigger { get; init; }
    public required DateTime Timestamp { get; init; }
    public required JsonDocument Payload { get; init; }

    public string? ChannelId { get; init; }
    public string? MessageId { get; init; }
    public Guid? TaskId { get; init; }
    public string? UserId { get; init; }

    /// <summary>
    /// Gets a string value from the payload by JSON path (e.g. "subject", "task.title").
    /// </summary>
    public string? GetProperty(string path)
    {
        var parts = path.Split('.');
        var current = Payload.RootElement;

        foreach (var part in parts)
        {
            if (current.TryGetProperty(part, out var next))
                current = next;
            else
                return null;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : current.GetRawText();
    }
}
