using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Skills.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Botty.Skills.Tests;

public class SchedulerSkillTests
{
    [Fact]
    public async Task ScheduleCronJob_ParsesSnakeCaseArguments()
    {
        var fakeScheduler = new FakeSchedulerService();
        var services = new ServiceCollection();
        services.AddSingleton<ISchedulerService>(fakeScheduler);
        var provider = services.BuildServiceProvider();

        var skill = new SchedulerSkill(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SchedulerSkill>.Instance);
        await skill.InitializeAsync(new SkillConfiguration
        {
            SkillId = "scheduler",
            Values = new Dictionary<string, string?>()
        });

        var result = await skill.ExecuteAsync(new SkillContext
        {
            ToolName = "schedule_cron_job",
            Arguments = """
            {
              "name": "daily_stock_update",
              "description": "Daily market summary",
              "cron_expression": "0 12 * * *",
              "task_title": "Send stock update",
              "task_description": "Compile and send summary"
            }
            """
        });

        Assert.True(result.Success, result.Error);
        Assert.NotNull(fakeScheduler.LastScheduledRequest);
        Assert.Equal("daily_stock_update", fakeScheduler.LastScheduledRequest!.Name);
        Assert.Equal("0 12 * * *", fakeScheduler.LastScheduledRequest.CronExpression);
        Assert.Equal("Send stock update", fakeScheduler.LastScheduledRequest.TaskTemplate.Title);
        Assert.Equal("Compile and send summary", fakeScheduler.LastScheduledRequest.TaskTemplate.Description);
        Assert.Equal(TaskAssignee.Assistant, fakeScheduler.LastScheduledRequest.TaskTemplate.Assignee);
        Assert.Equal(TaskType.General, fakeScheduler.LastScheduledRequest.TaskTemplate.Type);
        Assert.Equal(TaskPriority.Normal, fakeScheduler.LastScheduledRequest.TaskTemplate.Priority);
    }

    private sealed class FakeSchedulerService : ISchedulerService
    {
        public ScheduleTaskRequest? LastScheduledRequest { get; private set; }

        public Task<ScheduledTask> ScheduleTaskAsync(ScheduleTaskRequest request, CancellationToken ct = default)
        {
            LastScheduledRequest = request;
            var now = DateTime.UtcNow;
            return Task.FromResult(new ScheduledTask
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                CronExpression = request.CronExpression,
                TaskTemplate = request.TaskTemplate,
                IsRecurring = request.IsRecurring,
                MaxOccurrences = request.MaxOccurrences,
                CreatedBy = request.CreatedBy,
                IsActive = true,
                NextRunAt = now.AddMinutes(5),
                CreatedAt = now
            });
        }

        public bool ValidateCronExpression(string cronExpression) => true;
        public DateTime? GetNextRunTime(string cronExpression, DateTime? after = null) => DateTime.UtcNow.AddMinutes(5);

        public Task<ScheduledTask?> GetScheduledTaskAsync(Guid taskId, CancellationToken ct = default)
            => Task.FromResult<ScheduledTask?>(null);

        public Task<IEnumerable<ScheduledTask>> GetActiveScheduledTasksAsync(CancellationToken ct = default)
            => Task.FromResult(Enumerable.Empty<ScheduledTask>());

        public Task<ScheduledTask> UpdateScheduledTaskAsync(Guid taskId, UpdateScheduledTaskRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task CancelScheduledTaskAsync(Guid taskId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteScheduledTaskAsync(Guid taskId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
