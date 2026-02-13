using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Repository interface for scheduled task operations.
/// </summary>
public interface ISchedulerRepository
{
    /// <summary>
    /// Gets a scheduled task by ID.
    /// </summary>
    Task<ScheduledTask?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets all scheduled tasks.
    /// </summary>
    Task<IEnumerable<ScheduledTask>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);

    /// <summary>
    /// Gets all tasks that are due to run.
    /// </summary>
    Task<IEnumerable<ScheduledTask>> GetDueTasksAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new scheduled task.
    /// </summary>
    Task<ScheduledTask> CreateAsync(ScheduledTask task, CancellationToken ct = default);

    /// <summary>
    /// Updates a scheduled task.
    /// </summary>
    Task<ScheduledTask> UpdateAsync(ScheduledTask task, CancellationToken ct = default);

    /// <summary>
    /// Deletes a scheduled task.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Updates the next run time after execution.
    /// </summary>
    Task UpdateNextRunAsync(Guid id, DateTime nextRun, CancellationToken ct = default);

    /// <summary>
    /// Marks a task as executed and increments the occurrence count.
    /// </summary>
    Task MarkExecutedAsync(Guid id, CancellationToken ct = default);
}
