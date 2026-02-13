using System.Text.Json;
using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Hooks.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Hooks.Actions;

/// <summary>
/// Creates a Kanban task of type SendMessage (e.g. for WhatsApp) with optional approval.
/// </summary>
public class SendMessageAction : IHookAction
{
    public string Type => "send_message";
    private readonly IKanbanService _kanbanService;
    private readonly ILogger<SendMessageAction> _logger;

    public SendMessageAction(IKanbanService kanbanService, ILogger<SendMessageAction> logger)
    {
        _kanbanService = kanbanService;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(JsonDocument config, HookContext context, CancellationToken ct = default)
    {
        var root = config.RootElement;
        var to = root.GetProperty("to").GetString();
        var body = root.GetProperty("body").GetString();
        if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(body))
            return new ActionResult { Success = false, Error = "Missing to or body in action config" };

        to = ActionHelpers.SubstituteVariables(to, context);
        body = ActionHelpers.SubstituteVariables(body, context);
        var recipientName = root.TryGetProperty("recipientName", out var rn) ? ActionHelpers.SubstituteVariables(rn.GetString() ?? to, context) : to;

        var task = await _kanbanService.CreateTaskAsync(new CreateTaskRequest
        {
            Title = $"Send message to {recipientName}",
            Description = body,
            Assignee = TaskAssignee.Assistant,
            Type = TaskType.SendMessage,
            Priority = TaskPriority.Normal,
            Lane = KanbanLane.ToDo,
            PendingAction = new PendingAction
            {
                ActionType = "send_whatsapp",
                Description = $"Send WhatsApp to {recipientName}",
                Payload = new Dictionary<string, string> { ["to"] = to, ["body"] = body, ["recipientName"] = recipientName },
                Preview = body.Length > 100 ? body[..100] + "..." : body,
                RequiresApproval = true
            }
        }, ct);

        _logger.LogInformation("Hook queued send_message task {TaskId} to {To}", task.Id, to);
        return new ActionResult { Success = true, Output = task.Id.ToString() };
    }
}
