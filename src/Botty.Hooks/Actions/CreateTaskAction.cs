using System.Text.Json;
using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Hooks.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Hooks.Actions;

/// <summary>
/// Creates a Kanban task from hook config with template substitution.
/// </summary>
public class CreateTaskAction : IHookAction
{
    public string Type => "create_task";
    private readonly IKanbanService _kanbanService;
    private readonly ILogger<CreateTaskAction> _logger;

    public CreateTaskAction(IKanbanService kanbanService, ILogger<CreateTaskAction> logger)
    {
        _kanbanService = kanbanService;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(JsonDocument config, HookContext context, CancellationToken ct = default)
    {
        var root = config.RootElement;
        var title = root.GetProperty("title").GetString() ?? "Hook-created task";
        var description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null;
        title = ActionHelpers.SubstituteVariables(title, context);
        description = description != null ? ActionHelpers.SubstituteVariables(description, context) : null;

        var priority = TaskPriority.Normal;
        if (root.TryGetProperty("priority", out var pEl))
        {
            var pStr = pEl.GetString();
            if (Enum.TryParse<TaskPriority>(pStr, ignoreCase: true, out var p))
                priority = p;
        }

        var request = new CreateTaskRequest
        {
            Title = title,
            Description = description,
            Assignee = TaskAssignee.Assistant,
            Type = TaskType.General,
            Priority = priority,
            Lane = KanbanLane.ToDo
        };

        var task = await _kanbanService.CreateTaskAsync(request, ct);
        _logger.LogInformation("Hook created task {TaskId}: {Title}", task.Id, task.Title);
        return new ActionResult { Success = true, Output = task.Id.ToString() };
    }
}
