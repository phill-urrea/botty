using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace Botty.Tools.Scheduler;

/// <summary>
/// Tool for scheduling cron jobs and recurring tasks via the scheduler service.
/// </summary>
public class SchedulerTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SchedulerTool(
        IServiceScopeFactory scopeFactory,
        ILogger<SchedulerTool> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Id => "scheduler";
    public override string Name => "Scheduler";
    public override string Description => "Schedule cron jobs and recurring tasks (e.g. run something every day at 9am).";

    public override ToolConfigSchema ConfigSchema => new()
    {
        ToolId = Id,
        Fields = []
    };

    protected override Task OnInitializeAsync(CancellationToken ct) => Task.CompletedTask;

    public override IEnumerable<LlmTool> GetTools()
    {
        yield return new LlmTool
        {
            Name = "schedule_cron_job",
            Description = """
                Schedule a recurring task using a cron expression. Use for 'every day at 9am', 'daily at noon', 'every Monday', etc.
                IMPORTANT: Before calling this tool, gather enough context from the user to write a detailed prompt.
                For example, if they want a morning briefing, ask which stocks, what city for weather, what news topics, etc.
                The prompt field is critical — it tells the assistant exactly what to do each time the job runs.
                Convert the user's local time to UTC for the cron expression (e.g. 8:30am AEST = 22:30 UTC previous day).
                """,
            ParametersSchema = """
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string", "description": "Short name for the scheduled job" },
                    "description": { "type": "string", "description": "Brief human-readable description of what the job does" },
                    "cron_expression": { "type": "string", "description": "Cron expression: 5 fields (minute hour day-of-month month day-of-week), space-separated, in UTC. Examples: 0 12 * * * = daily at noon UTC, 30 22 * * * = daily at 10:30pm UTC" },
                    "prompt": { "type": "string", "description": "Detailed instruction for what the assistant should do each time this job runs. Be specific about what data to gather, what tools to use, and how to format the output. This prompt will be sent to the LLM with full tool access." },
                    "timezone": { "type": "string", "description": "IANA timezone of the user (e.g. 'Australia/Sydney', 'America/New_York', 'Europe/London'). Used for display purposes; cron_expression must still be in UTC." },
                    "task_title": { "type": "string", "description": "Title for the created task when the job runs" },
                    "task_description": { "type": "string", "description": "Fallback description if no prompt is provided" }
                },
                "required": ["name", "cron_expression", "prompt"]
            }
            """
        };
        yield return new LlmTool
        {
            Name = "list_scheduled_tasks",
            Description = "List all active scheduled tasks.",
            ParametersSchema = """
            {
                "type": "object",
                "properties": {},
                "required": []
            }
            """
        };
        yield return new LlmTool
        {
            Name = "cancel_scheduled_task",
            Description = "Cancel a scheduled task by ID.",
            ParametersSchema = """
            {
                "type": "object",
                "properties": {
                    "task_id": { "type": "string", "description": "The GUID of the scheduled task to cancel" }
                },
                "required": ["task_id"]
            }
            """
        };
    }

    protected override async Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct)
    {
        return context.ToolName switch
        {
            "schedule_cron_job" => await ScheduleCronJobAsync(context, ct),
            "list_scheduled_tasks" => await ListScheduledTasksAsync(ct),
            "cancel_scheduled_task" => await CancelScheduledTaskAsync(context, ct),
            _ => ToolResult.Fail($"Unknown tool: {context.ToolName}")
        };
    }

    private async Task<ToolResult> ScheduleCronJobAsync(ToolContext context, CancellationToken ct)
    {
        var args = ParseArguments<ScheduleCronJobArgs>(context.Arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.Name) || string.IsNullOrWhiteSpace(args.CronExpression))
        {
            return ToolResult.Fail("name and cron_expression are required.");
        }

        using var scope = _scopeFactory.CreateScope();
        var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();

        if (!schedulerService.ValidateCronExpression(args.CronExpression))
        {
            return ToolResult.Fail($"Invalid cron expression: {args.CronExpression}");
        }

        var request = new ScheduleTaskRequest
        {
            Name = args.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(args.Description) ? null : args.Description.Trim(),
            CronExpression = args.CronExpression.Trim(),
            IsRecurring = true,
            CreatedBy = "assistant",
            Prompt = string.IsNullOrWhiteSpace(args.Prompt) ? null : args.Prompt.Trim(),
            Timezone = string.IsNullOrWhiteSpace(args.Timezone) ? null : args.Timezone.Trim(),
            TaskTemplate = new KanbanTaskTemplate
            {
                Title = string.IsNullOrWhiteSpace(args.TaskTitle) ? args.Name.Trim() : args.TaskTitle.Trim(),
                Description = string.IsNullOrWhiteSpace(args.TaskDescription) ? null : args.TaskDescription.Trim(),
                Type = TaskType.General,
                Priority = TaskPriority.Normal,
                Assignee = TaskAssignee.Assistant,
                RequiresApproval = false
            }
        };

        try
        {
            var task = await schedulerService.ScheduleTaskAsync(request, ct);
            var nextRun = schedulerService.GetNextRunTime(request.CronExpression);
            return ToolResult.Ok(ToJson(new
            {
                scheduled_task_id = task.Id,
                name = task.Name,
                cron_expression = task.CronExpression,
                timezone = task.Timezone,
                has_prompt = !string.IsNullOrWhiteSpace(task.Prompt),
                next_run_at = nextRun
            }));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to schedule task {Name}", args.Name);
            return ToolResult.Fail(ex.Message);
        }
    }

    private async Task<ToolResult> ListScheduledTasksAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();
        try
        {
            var tasks = await schedulerService.GetActiveScheduledTasksAsync(ct);
            var list = tasks.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                description = t.Description,
                cron_expression = t.CronExpression,
                timezone = t.Timezone,
                has_prompt = !string.IsNullOrWhiteSpace(t.Prompt),
                next_run_at = schedulerService.GetNextRunTime(t.CronExpression, t.LastRunAt),
                is_recurring = t.IsRecurring
            }).ToList();
            return ToolResult.Ok(ToJson(list));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to list scheduled tasks");
            return ToolResult.Fail(ex.Message);
        }
    }

    private async Task<ToolResult> CancelScheduledTaskAsync(ToolContext context, CancellationToken ct)
    {
        var args = ParseArguments<CancelScheduledTaskArgs>(context.Arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.TaskId))
        {
            return ToolResult.Fail("task_id is required.");
        }

        if (!Guid.TryParse(args.TaskId, out var taskId))
        {
            return ToolResult.Fail("task_id must be a valid GUID.");
        }

        using var scope = _scopeFactory.CreateScope();
        var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();
        try
        {
            await schedulerService.CancelScheduledTaskAsync(taskId, ct);
            return ToolResult.Ok(ToJson(new { cancelled = taskId }));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to cancel scheduled task {TaskId}", args.TaskId);
            return ToolResult.Fail(ex.Message);
        }
    }

    private sealed class ScheduleCronJobArgs
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("cron_expression")]
        public string? CronExpression { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }

        [JsonPropertyName("task_title")]
        public string? TaskTitle { get; set; }

        [JsonPropertyName("task_description")]
        public string? TaskDescription { get; set; }
    }

    private sealed class CancelScheduledTaskArgs
    {
        [JsonPropertyName("task_id")]
        public string? TaskId { get; set; }
    }
}
