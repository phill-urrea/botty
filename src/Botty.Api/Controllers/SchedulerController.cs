using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Botty.Api.Controllers;

/// <summary>
/// API endpoints for scheduled task management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SchedulerController : ControllerBase
{
    private readonly ISchedulerService _schedulerService;
    private readonly ILogger<SchedulerController> _logger;

    public SchedulerController(
        ISchedulerService schedulerService,
        ILogger<SchedulerController> logger)
    {
        _schedulerService = schedulerService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all active scheduled tasks.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetScheduledTasks(CancellationToken ct)
    {
        var tasks = await _schedulerService.GetActiveScheduledTasksAsync(ct);
        return Ok(tasks.Select(MapToDto));
    }

    /// <summary>
    /// Gets a scheduled task by ID.
    /// </summary>
    [HttpGet("{taskId:guid}")]
    public async Task<IActionResult> GetScheduledTask(Guid taskId, CancellationToken ct)
    {
        var task = await _schedulerService.GetScheduledTaskAsync(taskId, ct);
        if (task == null)
        {
            return NotFound();
        }
        return Ok(MapToDto(task));
    }

    /// <summary>
    /// Creates a new scheduled task.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateScheduledTask([FromBody] CreateScheduledTaskDto request, CancellationToken ct)
    {
        // Validate cron expression
        if (!_schedulerService.ValidateCronExpression(request.CronExpression))
        {
            return BadRequest($"Invalid cron expression: {request.CronExpression}");
        }

        var scheduleRequest = new ScheduleTaskRequest
        {
            Name = request.Name,
            Description = request.Description,
            CronExpression = request.CronExpression,
            IsRecurring = request.IsRecurring ?? true,
            MaxOccurrences = request.MaxOccurrences,
            CreatedBy = request.CreatedBy ?? "user",
            TaskTemplate = new KanbanTaskTemplate
            {
                Title = request.TaskTitle,
                Description = request.TaskDescription,
                Type = request.TaskType ?? TaskType.General,
                Priority = request.TaskPriority ?? TaskPriority.Normal,
                Assignee = request.TaskAssignee ?? TaskAssignee.Assistant,
                RequiresApproval = request.RequiresApproval ?? false
            }
        };

        try
        {
            var task = await _schedulerService.ScheduleTaskAsync(scheduleRequest, ct);
            return CreatedAtAction(nameof(GetScheduledTask), new { taskId = task.Id }, MapToDto(task));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Updates a scheduled task.
    /// </summary>
    [HttpPut("{taskId:guid}")]
    public async Task<IActionResult> UpdateScheduledTask(Guid taskId, [FromBody] UpdateScheduledTaskDto request, CancellationToken ct)
    {
        // Validate cron expression if provided
        if (request.CronExpression != null && !_schedulerService.ValidateCronExpression(request.CronExpression))
        {
            return BadRequest($"Invalid cron expression: {request.CronExpression}");
        }

        var updateRequest = new UpdateScheduledTaskRequest
        {
            Name = request.Name,
            Description = request.Description,
            CronExpression = request.CronExpression,
            MaxOccurrences = request.MaxOccurrences,
            IsActive = request.IsActive
        };

        try
        {
            var task = await _schedulerService.UpdateScheduledTaskAsync(taskId, updateRequest, ct);
            return Ok(MapToDto(task));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Cancels a scheduled task (deactivates without deleting).
    /// </summary>
    [HttpPost("{taskId:guid}/cancel")]
    public async Task<IActionResult> CancelScheduledTask(Guid taskId, CancellationToken ct)
    {
        await _schedulerService.CancelScheduledTaskAsync(taskId, ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes a scheduled task.
    /// </summary>
    [HttpDelete("{taskId:guid}")]
    public async Task<IActionResult> DeleteScheduledTask(Guid taskId, CancellationToken ct)
    {
        await _schedulerService.DeleteScheduledTaskAsync(taskId, ct);
        return NoContent();
    }

    /// <summary>
    /// Validates a cron expression and returns the next run times.
    /// </summary>
    [HttpPost("validate")]
    public IActionResult ValidateCron([FromBody] ValidateCronDto request)
    {
        var isValid = _schedulerService.ValidateCronExpression(request.CronExpression);
        
        if (!isValid)
        {
            return Ok(new CronValidationResult
            {
                IsValid = false,
                Error = "Invalid cron expression"
            });
        }

        // Calculate next few occurrences
        var nextRuns = new List<DateTime>();
        var current = DateTime.UtcNow;
        
        for (var i = 0; i < 5; i++)
        {
            var next = _schedulerService.GetNextRunTime(request.CronExpression, current);
            if (next.HasValue)
            {
                nextRuns.Add(next.Value);
                current = next.Value.AddSeconds(1);
            }
            else
            {
                break;
            }
        }

        return Ok(new CronValidationResult
        {
            IsValid = true,
            NextOccurrences = nextRuns
        });
    }

    private static ScheduledTaskDto MapToDto(ScheduledTask task) => new()
    {
        Id = task.Id,
        Name = task.Name,
        Description = task.Description,
        CronExpression = task.CronExpression,
        NextRunAt = task.NextRunAt,
        LastRunAt = task.LastRunAt,
        IsRecurring = task.IsRecurring,
        MaxOccurrences = task.MaxOccurrences,
        OccurrenceCount = task.OccurrenceCount,
        IsActive = task.IsActive,
        CreatedBy = task.CreatedBy,
        CreatedAt = task.CreatedAt,
        TaskTemplate = new TaskTemplateDto
        {
            Title = task.TaskTemplate.Title,
            Description = task.TaskTemplate.Description,
            Type = task.TaskTemplate.Type.ToString(),
            Priority = task.TaskTemplate.Priority.ToString(),
            Assignee = task.TaskTemplate.Assignee.ToString(),
            RequiresApproval = task.TaskTemplate.RequiresApproval
        }
    };
}

#region DTOs

public class ScheduledTaskDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string CronExpression { get; set; }
    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public bool IsRecurring { get; set; }
    public int? MaxOccurrences { get; set; }
    public int OccurrenceCount { get; set; }
    public bool IsActive { get; set; }
    public required string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public required TaskTemplateDto TaskTemplate { get; set; }
}

public class TaskTemplateDto
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Type { get; set; }
    public required string Priority { get; set; }
    public required string Assignee { get; set; }
    public bool RequiresApproval { get; set; }
}

public class CreateScheduledTaskDto
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string CronExpression { get; set; }
    public bool? IsRecurring { get; set; }
    public int? MaxOccurrences { get; set; }
    public string? CreatedBy { get; set; }
    
    // Task template fields
    public required string TaskTitle { get; set; }
    public string? TaskDescription { get; set; }
    public TaskType? TaskType { get; set; }
    public TaskPriority? TaskPriority { get; set; }
    public TaskAssignee? TaskAssignee { get; set; }
    public bool? RequiresApproval { get; set; }
}

public class UpdateScheduledTaskDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? CronExpression { get; set; }
    public int? MaxOccurrences { get; set; }
    public bool? IsActive { get; set; }
}

public class ValidateCronDto
{
    public required string CronExpression { get; set; }
}

public class CronValidationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public List<DateTime>? NextOccurrences { get; set; }
}

#endregion
