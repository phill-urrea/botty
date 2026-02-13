using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botty.Scheduler.Services;

/// <summary>
/// Background service that processes scheduled tasks and creates Kanban tasks when due.
/// </summary>
public class SchedulerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchedulerBackgroundService> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    public SchedulerBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SchedulerBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduler background service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueTasksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled tasks");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("Scheduler background service stopping");
    }

    private async Task ProcessDueTasksAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var schedulerRepo = scope.ServiceProvider.GetRequiredService<ISchedulerRepository>();
        var kanbanRepo = scope.ServiceProvider.GetRequiredService<IKanbanRepository>();
        var cronParser = scope.ServiceProvider.GetRequiredService<ICronParser>();

        var dueTasks = await schedulerRepo.GetDueTasksAsync(ct);

        foreach (var scheduledTask in dueTasks)
        {
            try
            {
                await ProcessScheduledTaskAsync(scheduledTask, kanbanRepo, schedulerRepo, cronParser, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled task {Id}: {Name}",
                    scheduledTask.Id, scheduledTask.Name);
            }
        }
    }

    private async Task ProcessScheduledTaskAsync(
        ScheduledTask scheduledTask,
        IKanbanRepository kanbanRepo,
        ISchedulerRepository schedulerRepo,
        ICronParser cronParser,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing scheduled task: {Id} - {Name}",
            scheduledTask.Id, scheduledTask.Name);

        // Create a Kanban task from the template
        var template = scheduledTask.TaskTemplate;
        
        var kanbanTask = new KanbanTask
        {
            Title = template.Title,
            Description = template.Description,
            Lane = template.RequiresApproval ? KanbanLane.NeedsApproval : KanbanLane.ToDo,
            Assignee = template.Assignee,
            Type = template.Type,
            Priority = template.Priority,
            PendingActionData = template.PendingAction
        };

        await kanbanRepo.CreateAsync(kanbanTask, ct);

        _logger.LogInformation("Created Kanban task {Id} from scheduled task {ScheduledId}",
            kanbanTask.Id, scheduledTask.Id);

        // Calculate next run time
        if (scheduledTask.IsRecurring)
        {
            var nextRun = cronParser.GetNextOccurrence(scheduledTask.CronExpression, DateTime.UtcNow);
            
            if (nextRun.HasValue)
            {
                await schedulerRepo.UpdateNextRunAsync(scheduledTask.Id, nextRun.Value, ct);
                _logger.LogDebug("Next run for {Id} scheduled at {NextRun}",
                    scheduledTask.Id, nextRun.Value);
            }
            else
            {
                // Could not calculate next run - deactivate
                _logger.LogWarning("Could not calculate next run for {Id}, deactivating",
                    scheduledTask.Id);
                
                scheduledTask.IsActive = false;
                await schedulerRepo.UpdateAsync(scheduledTask, ct);
            }
        }
        else
        {
            // Non-recurring task - deactivate after execution
            scheduledTask.IsActive = false;
            scheduledTask.LastRunAt = DateTime.UtcNow;
            scheduledTask.OccurrenceCount++;
            await schedulerRepo.UpdateAsync(scheduledTask, ct);
        }
    }
}
