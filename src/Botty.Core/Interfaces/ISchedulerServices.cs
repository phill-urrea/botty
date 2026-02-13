using Botty.Core.Enums;
using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Service for managing scheduled tasks and cron jobs.
/// </summary>
public interface ISchedulerService
{
    /// <summary>
    /// Schedules a new task.
    /// </summary>
    Task<ScheduledTask> ScheduleTaskAsync(ScheduleTaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gets a scheduled task by ID.
    /// </summary>
    Task<ScheduledTask?> GetScheduledTaskAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Gets all active scheduled tasks.
    /// </summary>
    Task<IEnumerable<ScheduledTask>> GetActiveScheduledTasksAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates a scheduled task.
    /// </summary>
    Task<ScheduledTask> UpdateScheduledTaskAsync(Guid taskId, UpdateScheduledTaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// Cancels a scheduled task.
    /// </summary>
    Task CancelScheduledTaskAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a scheduled task.
    /// </summary>
    Task DeleteScheduledTaskAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Gets the next run time for a cron expression.
    /// </summary>
    DateTime? GetNextRunTime(string cronExpression, DateTime? after = null);

    /// <summary>
    /// Validates a cron expression.
    /// </summary>
    bool ValidateCronExpression(string cronExpression);
}

/// <summary>
/// Request to schedule a new task.
/// </summary>
public class ScheduleTaskRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string CronExpression { get; set; }
    public required KanbanTaskTemplate TaskTemplate { get; set; }
    public bool IsRecurring { get; set; } = true;
    public int? MaxOccurrences { get; set; }
    public string CreatedBy { get; set; } = "user";
}

/// <summary>
/// Request to update a scheduled task.
/// </summary>
public class UpdateScheduledTaskRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? CronExpression { get; set; }
    public KanbanTaskTemplate? TaskTemplate { get; set; }
    public int? MaxOccurrences { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Cron expression parser and calculator.
/// </summary>
public interface ICronParser
{
    /// <summary>
    /// Parses a cron expression and returns the next occurrence after the given time.
    /// </summary>
    DateTime? GetNextOccurrence(string cronExpression, DateTime after);

    /// <summary>
    /// Validates a cron expression.
    /// </summary>
    bool IsValid(string cronExpression);

    /// <summary>
    /// Gets multiple future occurrences.
    /// </summary>
    IEnumerable<DateTime> GetOccurrences(string cronExpression, DateTime start, DateTime end);
}
