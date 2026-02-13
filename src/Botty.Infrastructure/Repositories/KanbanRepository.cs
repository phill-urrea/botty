using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Botty.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Kanban task operations.
/// </summary>
public class KanbanRepository : IKanbanRepository
{
    private readonly BottyDbContext _context;

    public KanbanRepository(BottyDbContext context)
    {
        _context = context;
    }

    public async Task<KanbanTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.KanbanTasks
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<IEnumerable<KanbanTask>> GetByLaneAsync(KanbanLane lane, CancellationToken ct = default)
    {
        return await _context.KanbanTasks
            .Where(t => t.Lane == lane)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<KanbanTask>> GetByAssigneeAsync(TaskAssignee assignee, CancellationToken ct = default)
    {
        return await _context.KanbanTasks
            .Where(t => t.Assignee == assignee && t.Lane != KanbanLane.Done && t.Lane != KanbanLane.Cancelled)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<KanbanTask>> GetPendingApprovalAsync(CancellationToken ct = default)
    {
        return await _context.KanbanTasks
            .Where(t => t.Lane == KanbanLane.NeedsApproval)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<KanbanTask>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _context.KanbanTasks
            .Where(t => t.Lane != KanbanLane.Done && t.Lane != KanbanLane.Cancelled)
            .OrderBy(t => t.Lane)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<KanbanTask>> GetAllAsync(
        KanbanLane? lane = null,
        TaskAssignee? assignee = null,
        CancellationToken ct = default)
    {
        var query = _context.KanbanTasks.AsQueryable();

        if (lane.HasValue)
        {
            query = query.Where(t => t.Lane == lane.Value);
        }

        if (assignee.HasValue)
        {
            query = query.Where(t => t.Assignee == assignee.Value);
        }

        return await query
            .OrderBy(t => t.Lane)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<KanbanTask> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var task = await GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Task {id} not found");

        if (task.Lane != KanbanLane.NeedsApproval)
        {
            throw new InvalidOperationException($"Task {id} is not in NeedsApproval lane");
        }

        task.Lane = KanbanLane.InProgress;
        task.ApprovedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return task;
    }

    public async Task<KanbanTask> RejectAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var task = await GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Task {id} not found");

        if (task.Lane != KanbanLane.NeedsApproval)
        {
            throw new InvalidOperationException($"Task {id} is not in NeedsApproval lane");
        }

        task.Lane = KanbanLane.Cancelled;
        task.RejectionReason = reason;
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return task;
    }

    public async Task<KanbanTask> CreateAsync(KanbanTask task, CancellationToken ct = default)
    {
        task.Id = Guid.NewGuid();
        task.CreatedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;

        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync(ct);

        return task;
    }

    public async Task<KanbanTask> UpdateAsync(KanbanTask task, CancellationToken ct = default)
    {
        task.UpdatedAt = DateTime.UtcNow;
        
        _context.KanbanTasks.Update(task);
        await _context.SaveChangesAsync(ct);

        return task;
    }

    public async Task<KanbanTask> MoveToLaneAsync(Guid taskId, KanbanLane lane, CancellationToken ct = default)
    {
        var task = await GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found");

        var previousLane = task.Lane;
        task.Lane = lane;
        task.UpdatedAt = DateTime.UtcNow;

        // Track approval/completion timestamps
        if (lane == KanbanLane.NeedsApproval && previousLane != KanbanLane.NeedsApproval)
        {
            // Reset approval timestamp when entering approval
            task.ApprovedAt = null;
        }
        else if (lane != KanbanLane.NeedsApproval && previousLane == KanbanLane.NeedsApproval)
        {
            task.ApprovedAt = DateTime.UtcNow;
        }

        if (lane == KanbanLane.Done)
        {
            task.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);
        return task;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var task = await GetByIdAsync(id, ct);
        if (task != null)
        {
            _context.KanbanTasks.Remove(task);
            await _context.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Gets tasks assigned to the assistant that are ready to be worked on.
    /// </summary>
    public async Task<IEnumerable<KanbanTask>> GetAssistantReadyTasksAsync(CancellationToken ct = default)
    {
        return await _context.KanbanTasks
            .Where(t => t.Assignee == TaskAssignee.Assistant 
                && (t.Lane == KanbanLane.ToDo || t.Lane == KanbanLane.InProgress))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets recently approved tasks for the assistant to continue.
    /// </summary>
    public async Task<IEnumerable<KanbanTask>> GetRecentlyApprovedAsync(
        TimeSpan? since = null,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - (since ?? TimeSpan.FromMinutes(5));
        
        return await _context.KanbanTasks
            .Where(t => t.ApprovedAt != null 
                && t.ApprovedAt >= cutoff
                && t.Assignee == TaskAssignee.Assistant
                && (t.Lane == KanbanLane.ToDo || t.Lane == KanbanLane.InProgress))
            .OrderByDescending(t => t.ApprovedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets tasks by type.
    /// </summary>
    public async Task<IEnumerable<KanbanTask>> GetByTypeAsync(TaskType type, CancellationToken ct = default)
    {
        return await _context.KanbanTasks
            .Where(t => t.Type == type && t.Lane != KanbanLane.Done && t.Lane != KanbanLane.Cancelled)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }
}
