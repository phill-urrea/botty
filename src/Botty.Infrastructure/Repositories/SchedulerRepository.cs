using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Botty.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for scheduled task operations.
/// </summary>
public class SchedulerRepository : ISchedulerRepository
{
    private readonly BottyDbContext _context;

    public SchedulerRepository(BottyDbContext context)
    {
        _context = context;
    }

    public async Task<ScheduledTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<IEnumerable<ScheduledTask>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _context.ScheduledTasks
            .Where(t => t.IsActive)
            .OrderBy(t => t.NextRunAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ScheduledTask>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = _context.ScheduledTasks.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query
            .OrderBy(t => t.NextRunAt)
            .ToListAsync(ct);
    }

    public async Task MarkExecutedAsync(Guid id, CancellationToken ct = default)
    {
        var task = await GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Scheduled task {id} not found");

        task.LastRunAt = DateTime.UtcNow;
        task.OccurrenceCount++;

        // Deactivate if max occurrences reached
        if (task.MaxOccurrences.HasValue && task.OccurrenceCount >= task.MaxOccurrences.Value)
        {
            task.IsActive = false;
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<ScheduledTask>> GetDueTasksAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        
        return await _context.ScheduledTasks
            .Where(t => t.IsActive && t.NextRunAt <= now)
            .OrderBy(t => t.NextRunAt)
            .ToListAsync(ct);
    }

    public async Task<ScheduledTask> CreateAsync(ScheduledTask task, CancellationToken ct = default)
    {
        task.Id = Guid.NewGuid();
        task.CreatedAt = DateTime.UtcNow;

        _context.ScheduledTasks.Add(task);
        await _context.SaveChangesAsync(ct);

        return task;
    }

    public async Task<ScheduledTask> UpdateAsync(ScheduledTask task, CancellationToken ct = default)
    {
        _context.ScheduledTasks.Update(task);
        await _context.SaveChangesAsync(ct);

        return task;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var task = await GetByIdAsync(id, ct);
        if (task != null)
        {
            _context.ScheduledTasks.Remove(task);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateNextRunAsync(Guid id, DateTime nextRun, CancellationToken ct = default)
    {
        var task = await GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Scheduled task {id} not found");

        task.LastRunAt = DateTime.UtcNow;
        task.NextRunAt = nextRun;
        task.OccurrenceCount++;

        // Deactivate if max occurrences reached
        if (task.MaxOccurrences.HasValue && task.OccurrenceCount >= task.MaxOccurrences.Value)
        {
            task.IsActive = false;
        }

        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Gets scheduled tasks by creator.
    /// </summary>
    public async Task<IEnumerable<ScheduledTask>> GetByCreatorAsync(
        string createdBy,
        CancellationToken ct = default)
    {
        return await _context.ScheduledTasks
            .Where(t => t.CreatedBy == createdBy)
            .OrderBy(t => t.NextRunAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Deactivates a scheduled task.
    /// </summary>
    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var task = await GetByIdAsync(id, ct);
        if (task != null)
        {
            task.IsActive = false;
            await _context.SaveChangesAsync(ct);
        }
    }
}
