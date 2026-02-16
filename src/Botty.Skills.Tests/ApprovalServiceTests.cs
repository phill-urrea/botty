using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Hooks;
using Botty.Hooks.Models;
using Botty.Workflow.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Botty.Skills.Tests;

public class ApprovalServiceTests
{
    [Fact]
    public async Task RequestApproval_PersistsAssigneeAndContext()
    {
        var repo = new FakeKanbanRepository();
        var hooks = new FakeHookRegistry();
        var service = new ApprovalService(repo, NullLogger<ApprovalService>.Instance, hooks);
        var conversationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var created = await service.RequestApprovalAsync(new ApprovalRequest
        {
            Title = "Approve tool call",
            Description = "desc",
            Type = TaskType.SendMessage,
            Assignee = TaskAssignee.Assistant,
            ConversationId = conversationId,
            UserId = userId,
            Source = "admin",
            ExternalId = "default",
            Action = new PendingAction
            {
                ActionType = "ExecuteSkillTool",
                Payload = new Dictionary<string, string>
                {
                    ["toolName"] = "channel_send_message",
                    ["arguments"] = "{}"
                }
            }
        });

        Assert.Equal(TaskAssignee.Assistant, created.Assignee);
        Assert.Equal(conversationId, created.ConversationId);
        Assert.Equal(userId, created.UserId);
        Assert.Equal("admin", created.Source);
        Assert.Equal("default", created.ExternalId);
    }

    [Fact]
    public async Task Approve_AssistantTask_PublishesTaskApprovedHook()
    {
        var repo = new FakeKanbanRepository();
        var hooks = new FakeHookRegistry();
        var service = new ApprovalService(repo, NullLogger<ApprovalService>.Instance, hooks);
        var task = await repo.CreateAsync(new KanbanTask
        {
            Title = "approve me",
            Lane = KanbanLane.NeedsApproval,
            Assignee = TaskAssignee.Assistant,
            Type = TaskType.SendMessage,
            Priority = TaskPriority.Normal,
            PendingActionData = new PendingAction { ActionType = "ExecuteSkillTool" }
        });

        var approved = await service.ApproveAsync(task.Id, ct: CancellationToken.None);

        Assert.Equal(KanbanLane.InProgress, approved.Lane);
        Assert.Contains(hooks.PublishedEvents, e => e.Trigger == HookTrigger.TaskApproved);
    }

    private sealed class FakeKanbanRepository : IKanbanRepository
    {
        private readonly Dictionary<Guid, KanbanTask> _tasks = new();

        public Task<KanbanTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            _tasks.TryGetValue(id, out var task);
            return Task.FromResult(task);
        }

        public Task<IEnumerable<KanbanTask>> GetAllAsync(KanbanLane? lane = null, TaskAssignee? assignee = null, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<KanbanTask>>(_tasks.Values);

        public Task<IEnumerable<KanbanTask>> GetByLaneAsync(KanbanLane lane, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<KanbanTask>>(_tasks.Values.Where(t => t.Lane == lane).ToList());

        public Task<IEnumerable<KanbanTask>> GetByAssigneeAsync(TaskAssignee assignee, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<KanbanTask>>(_tasks.Values.Where(t => t.Assignee == assignee).ToList());

        public Task<KanbanTask> CreateAsync(KanbanTask task, CancellationToken ct = default)
        {
            task.Id = task.Id == Guid.Empty ? Guid.NewGuid() : task.Id;
            task.CreatedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            _tasks[task.Id] = task;
            return Task.FromResult(task);
        }

        public Task<KanbanTask> UpdateAsync(KanbanTask task, CancellationToken ct = default)
        {
            task.UpdatedAt = DateTime.UtcNow;
            _tasks[task.Id] = task;
            return Task.FromResult(task);
        }

        public async Task<KanbanTask> MoveToLaneAsync(Guid id, KanbanLane lane, CancellationToken ct = default)
        {
            var task = await GetByIdAsync(id, ct) ?? throw new InvalidOperationException("missing task");
            task.Lane = lane;
            if (lane != KanbanLane.NeedsApproval)
                task.ApprovedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            _tasks[id] = task;
            return task;
        }

        public Task<KanbanTask> ApproveAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<KanbanTask> RejectAsync(Guid id, string reason, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _tasks.Remove(id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHookRegistry : IHookRegistry
    {
        public List<(HookTrigger Trigger, object Payload)> PublishedEvents { get; } = [];

        public void Register(IHook hook) { }
        public void Unregister(string hookId) { }
        public IHook? GetHook(string hookId) => null;
        public IEnumerable<IHook> GetHooksForTrigger(HookTrigger trigger) => [];
        public IEnumerable<IHook> GetAllHooks() => [];
        public Task<IEnumerable<HookResult>> TriggerAsync(HookTrigger trigger, HookContext context, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<HookResult>>([]);

        public Task PublishEventAsync(HookTrigger trigger, object payload, CancellationToken ct = default)
        {
            PublishedEvents.Add((trigger, payload));
            return Task.CompletedTask;
        }
    }
}
