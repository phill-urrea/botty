using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Scheduler.Services;

/// <summary>
/// Service for managing scheduled tasks and cron jobs.
/// </summary>
public class SchedulerService : ISchedulerService
{
    private readonly ISchedulerRepository _repository;
    private readonly ICronParser _cronParser;
    private readonly ILogger<SchedulerService> _logger;

    public SchedulerService(
        ISchedulerRepository repository,
        ICronParser cronParser,
        ILogger<SchedulerService> logger)
    {
        _repository = repository;
        _cronParser = cronParser;
        _logger = logger;
    }

    public async Task<ScheduledTask> ScheduleTaskAsync(ScheduleTaskRequest request, CancellationToken ct = default)
    {
        if (!ValidateCronExpression(request.CronExpression))
        {
            throw new ArgumentException($"Invalid cron expression: {request.CronExpression}");
        }

        var nextRun = GetNextRunTime(request.CronExpression);
        if (nextRun == null)
        {
            _logger.LogWarning("Could not calculate next run time for cron expression: {CronExpression}", request.CronExpression);
            throw new ArgumentException($"Could not calculate next run time for expression: {request.CronExpression}");
        }

        var nextRunAt = nextRun.Value;
        _logger.LogInformation("Scheduling task: {Name} with cron: {Cron}, next run: {NextRun}",
            request.Name, request.CronExpression, nextRunAt);

        var task = new ScheduledTask
        {
            Name = request.Name,
            Description = request.Description,
            CronExpression = request.CronExpression,
            NextRunAt = nextRunAt,
            TaskTemplate = request.TaskTemplate,
            Assignee = request.TaskTemplate.Assignee,
            IsRecurring = request.IsRecurring,
            MaxOccurrences = request.MaxOccurrences,
            IsActive = true,
            CreatedBy = request.CreatedBy,
            Prompt = request.Prompt,
            Timezone = request.Timezone
        };

        return await _repository.CreateAsync(task, ct);
    }

    public async Task<ScheduledTask?> GetScheduledTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        return await _repository.GetByIdAsync(taskId, ct);
    }

    public async Task<IEnumerable<ScheduledTask>> GetActiveScheduledTasksAsync(CancellationToken ct = default)
    {
        return await _repository.GetAllAsync(includeInactive: false, ct);
    }

    public async Task<IEnumerable<ScheduledTask>> GetAllScheduledTasksAsync(CancellationToken ct = default)
    {
        return await _repository.GetAllAsync(includeInactive: true, ct);
    }

    public async Task<ScheduledTask> UpdateScheduledTaskAsync(
        Guid taskId,
        UpdateScheduledTaskRequest request,
        CancellationToken ct = default)
    {
        var task = await _repository.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"Scheduled task {taskId} not found");

        if (request.Name != null)
            task.Name = request.Name;
        if (request.Description != null)
            task.Description = request.Description;
        if (request.Prompt != null)
            task.Prompt = request.Prompt;
        if (request.Timezone != null)
            task.Timezone = request.Timezone;
        if (request.TaskTemplate != null)
            task.TaskTemplate = request.TaskTemplate;
        if (request.MaxOccurrences.HasValue)
            task.MaxOccurrences = request.MaxOccurrences;
        if (request.IsActive.HasValue)
            task.IsActive = request.IsActive.Value;

        if (request.CronExpression != null)
        {
            if (!ValidateCronExpression(request.CronExpression))
            {
                throw new ArgumentException($"Invalid cron expression: {request.CronExpression}");
            }

            task.CronExpression = request.CronExpression;
            task.NextRunAt = GetNextRunTime(request.CronExpression) ?? DateTime.UtcNow.AddDays(1);
        }

        _logger.LogInformation("Updating scheduled task: {Id}", taskId);

        return await _repository.UpdateAsync(task, ct);
    }

    public async Task CancelScheduledTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        _logger.LogInformation("Cancelling scheduled task: {Id}", taskId);
        
        var task = await _repository.GetByIdAsync(taskId, ct);
        if (task != null)
        {
            task.IsActive = false;
            await _repository.UpdateAsync(task, ct);
        }
    }

    public async Task RunNowAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await _repository.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"Scheduled task {taskId} not found");

        // Set next run to now so the background service picks it up on the next poll
        task.NextRunAt = DateTime.UtcNow;
        if (!task.IsActive)
            task.IsActive = true;
        await _repository.UpdateAsync(task, ct);

        _logger.LogInformation("Triggered immediate run for scheduled task: {Id} ({Name})", taskId, task.Name);
    }

    public async Task DeleteScheduledTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting scheduled task: {Id}", taskId);
        await _repository.DeleteAsync(taskId, ct);
    }

    public DateTime? GetNextRunTime(string cronExpression, DateTime? after = null)
    {
        return _cronParser.GetNextOccurrence(cronExpression, after ?? DateTime.UtcNow);
    }

    public bool ValidateCronExpression(string cronExpression)
    {
        return _cronParser.IsValid(cronExpression);
    }
}
