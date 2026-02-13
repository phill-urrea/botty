using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Skills.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Botty.Skills.Scheduler;

/// <summary>
/// Skill for scheduling cron jobs and recurring tasks via the scheduler service.
/// </summary>
public class SchedulerSkill : BaseSkill
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SchedulerSkill(
        IServiceScopeFactory scopeFactory,
        ILogger<SchedulerSkill> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Id => "scheduler";
    public override string Name => "Scheduler";
    public override string Description => "Schedule cron jobs and recurring tasks (e.g. run something every day at 9am).";

    public override SkillConfigSchema ConfigSchema => new()
    {
        SkillId = Id,
        Fields = []
    };

    protected override Task OnInitializeAsync(CancellationToken ct) => Task.CompletedTask;

    public override IEnumerable<LlmTool> GetTools()
    {
        yield return new LlmTool
        {
            Name = "schedule_cron_job",
            Description = "Schedule a recurring task using a cron expression. Use for 'every day at 9am', 'daily at noon', 'every Monday', etc.",
            ParametersSchema = """
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string", "description": "Short name for the scheduled job" },
                    "description": { "type": "string", "description": "Optional description of what the job does" },
                    "cron_expression": { "type": "string", "description": "Cron expression: 5 fields (minute hour day-of-month month day-of-week), space-separated, UTC. Examples: 0 12 * * * = daily at noon, 0 9 * * * = daily at 9am, 0 9 * * 1 = Mondays at 9am" },
                    "task_title": { "type": "string", "description": "Title for the created task when the job runs" },
                    "task_description": { "type": "string", "description": "Optional description for the created task" }
                },
                "required": ["name", "cron_expression"]
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

    protected override async Task<SkillResult> OnExecuteAsync(SkillContext context, CancellationToken ct)
    {
        return context.ToolName switch
        {
            "schedule_cron_job" => await ScheduleCronJobAsync(context, ct),
            "list_scheduled_tasks" => await ListScheduledTasksAsync(ct),
            "cancel_scheduled_task" => await CancelScheduledTaskAsync(context, ct),
            _ => SkillResult.Fail($"Unknown tool: {context.ToolName}")
        };
    }

    private async Task<SkillResult> ScheduleCronJobAsync(SkillContext context, CancellationToken ct)
    {
        var args = ParseArguments<ScheduleCronJobArgs>(context.Arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.Name) || string.IsNullOrWhiteSpace(args.CronExpression))
        {
            return SkillResult.Fail("name and cron_expression are required.");
        }

        using var scope = _scopeFactory.CreateScope();
        var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();

        if (!schedulerService.ValidateCronExpression(args.CronExpression))
        {
            return SkillResult.Fail($"Invalid cron expression: {args.CronExpression}");
        }

        var request = new ScheduleTaskRequest
        {
            Name = args.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(args.Description) ? null : args.Description.Trim(),
            CronExpression = args.CronExpression.Trim(),
            IsRecurring = true,
            CreatedBy = "assistant",
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
            return SkillResult.Ok(ToJson(new
            {
                scheduled_task_id = task.Id,
                name = task.Name,
                cron_expression = task.CronExpression,
                next_run_at = nextRun
            }));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to schedule task {Name}", args.Name);
            return SkillResult.Fail(ex.Message);
        }
    }

    private async Task<SkillResult> ListScheduledTasksAsync(CancellationToken ct)
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
                next_run_at = schedulerService.GetNextRunTime(t.CronExpression, t.LastRunAt),
                is_recurring = t.IsRecurring
            }).ToList();
            return SkillResult.Ok(ToJson(list));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to list scheduled tasks");
            return SkillResult.Fail(ex.Message);
        }
    }

    private async Task<SkillResult> CancelScheduledTaskAsync(SkillContext context, CancellationToken ct)
    {
        var args = ParseArguments<CancelScheduledTaskArgs>(context.Arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.TaskId))
        {
            return SkillResult.Fail("task_id is required.");
        }

        if (!Guid.TryParse(args.TaskId, out var taskId))
        {
            return SkillResult.Fail("task_id must be a valid GUID.");
        }

        using var scope = _scopeFactory.CreateScope();
        var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();
        try
        {
            await schedulerService.CancelScheduledTaskAsync(taskId, ct);
            return SkillResult.Ok(ToJson(new { cancelled = taskId }));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to cancel scheduled task {TaskId}", args.TaskId);
            return SkillResult.Fail(ex.Message);
        }
    }

    private sealed class ScheduleCronJobArgs
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? CronExpression { get; set; }
        public string? TaskTitle { get; set; }
        public string? TaskDescription { get; set; }
    }

    private sealed class CancelScheduledTaskArgs
    {
        public string? TaskId { get; set; }
    }
}
