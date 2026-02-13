using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Hooks;
using Botty.Hooks.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Workflow.Services;

/// <summary>
/// Service for managing Kanban tasks.
/// </summary>
public class KanbanService : IKanbanService
{
    private readonly IKanbanRepository _repository;
    private readonly IHookRegistry? _hookRegistry;
    private readonly ILogger<KanbanService> _logger;

    public KanbanService(
        IKanbanRepository repository,
        ILogger<KanbanService> logger,
        IHookRegistry? hookRegistry = null)
    {
        _repository = repository;
        _logger = logger;
        _hookRegistry = hookRegistry;
    }

    public async Task<KanbanTask> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating task: {Title} in lane {Lane}", request.Title, request.Lane);

        var task = new KanbanTask
        {
            Title = request.Title,
            Description = request.Description,
            Lane = request.Lane,
            Assignee = request.Assignee,
            Type = request.Type,
            Priority = request.Priority,
            PendingActionData = request.PendingAction
        };

        task = await _repository.CreateAsync(task, ct);
        if (_hookRegistry != null)
            await _hookRegistry.PublishEventAsync(HookTrigger.TaskCreated, new { TaskId = task.Id, Task = task }, ct);
        return task;
    }

    public async Task<KanbanTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        return await _repository.GetByIdAsync(taskId, ct);
    }

    public async Task<IEnumerable<KanbanTask>> GetActiveTasksAsync(CancellationToken ct = default)
    {
        // Get all tasks except Done and Cancelled
        var all = await _repository.GetAllAsync(ct: ct);
        return all.Where(t => t.Lane != KanbanLane.Done && t.Lane != KanbanLane.Cancelled);
    }

    public async Task<IEnumerable<KanbanTask>> GetTasksByLaneAsync(KanbanLane lane, CancellationToken ct = default)
    {
        return await _repository.GetByLaneAsync(lane, ct);
    }

    public async Task<IEnumerable<KanbanTask>> GetTasksByAssigneeAsync(TaskAssignee assignee, CancellationToken ct = default)
    {
        return await _repository.GetByAssigneeAsync(assignee, ct);
    }

    public async Task<KanbanTask> MoveTaskAsync(Guid taskId, KanbanLane lane, CancellationToken ct = default)
    {
        var task = await _repository.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found");

        var previousLane = task.Lane;

        _logger.LogInformation("Moving task {Id} from {PreviousLane} to {NewLane}",
            taskId, previousLane, lane);

        task = await _repository.MoveToLaneAsync(taskId, lane, ct);
        if (_hookRegistry != null)
        {
            await _hookRegistry.PublishEventAsync(HookTrigger.TaskMoved, new
            {
                TaskId = taskId,
                OldLane = previousLane.ToString(),
                NewLane = lane.ToString(),
                Task = task
            }, ct);
            if (lane == KanbanLane.Done && previousLane == KanbanLane.NeedsApproval)
                await _hookRegistry.PublishEventAsync(HookTrigger.TaskApproved, new { TaskId = taskId, Task = task }, ct);
            if (lane == KanbanLane.Done)
                await _hookRegistry.PublishEventAsync(HookTrigger.TaskCompleted, new { TaskId = taskId, Task = task }, ct);
        }
        return task;
    }

    public async Task<KanbanTask> UpdateTaskAsync(Guid taskId, UpdateTaskRequest request, CancellationToken ct = default)
    {
        var task = await _repository.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found");

        if (request.Title != null)
            task.Title = request.Title;
        if (request.Description != null)
            task.Description = request.Description;
        if (request.Assignee.HasValue)
            task.Assignee = request.Assignee.Value;
        if (request.Priority.HasValue)
            task.Priority = request.Priority.Value;
        if (request.ExecutionResult != null)
            task.ExecutionResult = request.ExecutionResult;

        _logger.LogInformation("Updating task {Id}", taskId);

        return await _repository.UpdateAsync(task, ct);
    }

    public async Task DeleteTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting task {Id}", taskId);
        await _repository.DeleteAsync(taskId, ct);
    }

    public async Task<KanbanBoard> GetBoardAsync(CancellationToken ct = default)
    {
        var tasks = await _repository.GetAllAsync(ct: ct);
        var taskList = tasks.ToList();

        return new KanbanBoard
        {
            ToDo = taskList.Where(t => t.Lane == KanbanLane.ToDo).ToList(),
            InProgress = taskList.Where(t => t.Lane == KanbanLane.InProgress).ToList(),
            NeedsApproval = taskList.Where(t => t.Lane == KanbanLane.NeedsApproval).ToList(),
            Done = taskList.Where(t => t.Lane == KanbanLane.Done).Take(20).ToList(),
            Cancelled = taskList.Where(t => t.Lane == KanbanLane.Cancelled).Take(10).ToList()
        };
    }
}
