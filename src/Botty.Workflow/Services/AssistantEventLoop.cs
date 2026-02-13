using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Channels;
using Botty.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botty.Workflow.Services;

/// <summary>
/// Background service that monitors the Kanban board and acts on tasks assigned to the assistant.
/// This is the main "brain" of the assistant that picks up work and executes it.
/// </summary>
public class AssistantEventLoop : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AssistantEventLoop> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    public AssistantEventLoop(
        IServiceScopeFactory scopeFactory,
        ILogger<AssistantEventLoop> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Assistant event loop starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingTasksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in assistant event loop");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("Assistant event loop stopping");
    }

    private async Task ProcessPendingTasksAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<KanbanRepository>();
        var kanbanService = scope.ServiceProvider.GetRequiredService<IKanbanService>();

        // Get tasks assigned to assistant that are ready to work on
        var readyTasks = await repository.GetAssistantReadyTasksAsync(ct);
        var processedTaskIds = new HashSet<Guid>();
        
        foreach (var task in readyTasks)
        {
            try
            {
                await ProcessTaskAsync(task, kanbanService, scope.ServiceProvider, ct);
                processedTaskIds.Add(task.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing task {Id}: {Title}", task.Id, task.Title);
                
                // Mark task with error
                await kanbanService.UpdateTaskAsync(task.Id, new UpdateTaskRequest
                {
                    ExecutionResult = $"Error: {ex.Message}"
                }, ct);
                if (task.PendingActionData != null)
                {
                    await kanbanService.MoveTaskAsync(task.Id, KanbanLane.Cancelled, ct);
                }
                processedTaskIds.Add(task.Id);
            }
        }

        // Check for recently approved tasks that need continuation
        var approvedTasks = await repository.GetRecentlyApprovedAsync(ct: ct);
        
        foreach (var task in approvedTasks)
        {
            if (processedTaskIds.Contains(task.Id))
            {
                continue;
            }

            if (task.PendingActionData != null)
            {
                try
                {
                    await ExecutePendingActionAsync(task, kanbanService, scope.ServiceProvider, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing approved action for task {Id}", task.Id);
                    
                    await kanbanService.UpdateTaskAsync(task.Id, new UpdateTaskRequest
                    {
                        ExecutionResult = $"Execution failed: {ex.Message}"
                    }, ct);

                    await kanbanService.MoveTaskAsync(task.Id, KanbanLane.Cancelled, ct);
                }
            }
        }
    }

    private async Task ProcessTaskAsync(
        KanbanTask task,
        IKanbanService kanbanService,
        IServiceProvider services,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing task {Id}: {Title} (type: {Type})", 
            task.Id, task.Title, task.Type);

        // Move to InProgress if in ToDo
        if (task.Lane == KanbanLane.ToDo)
        {
            await kanbanService.MoveTaskAsync(task.Id, KanbanLane.InProgress, ct);
        }

        // Check if this task type requires approval before execution
        var approvalService = services.GetRequiredService<IApprovalService>();
        
        if (approvalService.RequiresApproval(task.Type) && task.ApprovedAt == null)
        {
            _logger.LogInformation("Task {Id} requires approval, moving to NeedsApproval", task.Id);
            await kanbanService.MoveTaskAsync(task.Id, KanbanLane.NeedsApproval, ct);
            return;
        }

        // Approved tasks with pending action should always execute from pending-action payload.
        if (task.PendingActionData != null && task.ApprovedAt != null)
        {
            await ExecutePendingActionAsync(task, kanbanService, services, ct);
            return;
        }

        // Execute the task based on type
        await ExecuteTaskAsync(task, kanbanService, services, ct);
    }

    private async Task ExecuteTaskAsync(
        KanbanTask task,
        IKanbanService kanbanService,
        IServiceProvider services,
        CancellationToken ct)
    {
        var result = task.Type switch
        {
            TaskType.General => await ExecuteGeneralTaskAsync(task, services, ct),
            TaskType.SendMessage => await ExecuteSendMessageAsync(task, services, ct),
            TaskType.SystemChange => await ExecuteSystemChangeAsync(task, services, ct),
            TaskType.SkillExecution => await ExecuteSkillAsync(task, services, ct),
            TaskType.NewSkillCreation => await ExecuteNewSkillCreationAsync(task, services, ct),
            TaskType.ShellCommand => await ExecuteShellCommandAsync(task, services, ct),
            TaskType.MemoryModification => await ExecuteMemoryModificationAsync(task, services, ct),
            TaskType.CalendarChange => await ExecuteCalendarChangeAsync(task, services, ct),
            _ => $"Unknown task type: {task.Type}"
        };

        // Update task with result
        await kanbanService.UpdateTaskAsync(task.Id, new UpdateTaskRequest
        {
            ExecutionResult = result
        }, ct);

        // Move to Done
        await kanbanService.MoveTaskAsync(task.Id, KanbanLane.Done, ct);

        _logger.LogInformation("Task {Id} completed: {Result}", 
            task.Id, result.Length > 100 ? result[..100] + "..." : result);
    }

    private async Task ExecutePendingActionAsync(
        KanbanTask task,
        IKanbanService kanbanService,
        IServiceProvider services,
        CancellationToken ct)
    {
        if (task.PendingActionData == null)
        {
            return;
        }

        _logger.LogInformation("Executing approved pending action for task {Id}: {ActionType}",
            task.Id, task.PendingActionData.ActionType);

        // Move to InProgress
        await kanbanService.MoveTaskAsync(task.Id, KanbanLane.InProgress, ct);

        // Execute based on the pending action
        var actionType = task.PendingActionData.ActionType.Trim().ToLowerInvariant();
        string result = actionType switch
        {
            "sendmessage" => await ExecuteSendMessageFromActionAsync(task.PendingActionData, services, ct),
            "send_whatsapp" => await ExecuteSendWhatsAppFromActionAsync(task.PendingActionData, services, ct),
            "sendwhatsapp" => await ExecuteSendWhatsAppFromActionAsync(task.PendingActionData, services, ct),
            "shellcommand" => await ExecuteShellFromActionAsync(task.PendingActionData, services, ct),
            "systemchange" => await ExecuteSystemChangeFromActionAsync(task.PendingActionData, services, ct),
            "executeskilltool" => await ExecuteSkillToolFromActionAsync(task, task.PendingActionData, services, ct),
            _ => $"Executed action: {task.PendingActionData.ActionType}"
        };

        await kanbanService.UpdateTaskAsync(task.Id, new UpdateTaskRequest
        {
            ExecutionResult = result
        }, ct);

        await kanbanService.MoveTaskAsync(task.Id, KanbanLane.Done, ct);
    }

    // Task execution methods - these will be expanded as more services are implemented

    private Task<string> ExecuteGeneralTaskAsync(KanbanTask task, IServiceProvider services, CancellationToken ct)
    {
        // General tasks might involve LLM reasoning
        return Task.FromResult($"General task completed: {task.Title}");
    }

    private Task<string> ExecuteSendMessageAsync(KanbanTask task, IServiceProvider services, CancellationToken ct)
    {
        // Will integrate with messaging service in Phase 5
        return Task.FromResult("Message sending not yet implemented (Phase 5)");
    }

    private Task<string> ExecuteSystemChangeAsync(KanbanTask task, IServiceProvider services, CancellationToken ct)
    {
        // System configuration changes
        return Task.FromResult("System change executed");
    }

    private Task<string> ExecuteSkillAsync(KanbanTask task, IServiceProvider services, CancellationToken ct)
    {
        // Will integrate with skills system in Phase 6
        return Task.FromResult("Skill execution not yet implemented (Phase 6)");
    }

    private Task<string> ExecuteNewSkillCreationAsync(KanbanTask task, IServiceProvider services, CancellationToken ct)
    {
        // Will allow assistant to create new skills in Phase 6
        return Task.FromResult("New skill creation not yet implemented (Phase 6)");
    }

    private Task<string> ExecuteShellCommandAsync(KanbanTask task, IServiceProvider services, CancellationToken ct)
    {
        // Shell command execution will be implemented in Phase 6
        return Task.FromResult("Shell command execution not yet implemented (Phase 6)");
    }

    private Task<string> ExecuteMemoryModificationAsync(KanbanTask task, IServiceProvider services, CancellationToken ct)
    {
        // Memory modifications (forget, correct) that require approval
        return Task.FromResult("Memory modification executed");
    }

    private Task<string> ExecuteCalendarChangeAsync(KanbanTask task, IServiceProvider services, CancellationToken ct)
    {
        // Calendar integration in Phase 6
        return Task.FromResult("Calendar change not yet implemented (Phase 6)");
    }

    // Pending action execution methods

    private Task<string> ExecuteSendMessageFromActionAsync(
        PendingAction action, IServiceProvider services, CancellationToken ct)
    {
        var payload = action.Payload;
        var channelId = payload?.GetValueOrDefault("channel_id")
            ?? payload?.GetValueOrDefault("channelId");
        var chatId = payload?.GetValueOrDefault("chat_id")
            ?? payload?.GetValueOrDefault("chatId")
            ?? payload?.GetValueOrDefault("to");
        var text = payload?.GetValueOrDefault("message")
            ?? payload?.GetValueOrDefault("text")
            ?? payload?.GetValueOrDefault("body");
        var replyToMessageId = payload?.GetValueOrDefault("reply_to_message_id")
            ?? payload?.GetValueOrDefault("replyToMessageId");

        if (!string.IsNullOrWhiteSpace(channelId) &&
            !string.IsNullOrWhiteSpace(chatId) &&
            !string.IsNullOrWhiteSpace(text))
        {
            return ExecuteChannelSendAsync(
                services,
                channelId.Trim(),
                chatId.Trim(),
                text.Trim(),
                replyToMessageId,
                ct);
        }

        return Task.FromResult($"Message action is missing channel/chat/message payload fields.");
    }

    private Task<string> ExecuteSendWhatsAppFromActionAsync(
        PendingAction action, IServiceProvider services, CancellationToken ct)
    {
        var payload = action.Payload;
        var chatId = payload?.GetValueOrDefault("to")
            ?? payload?.GetValueOrDefault("chat_id")
            ?? payload?.GetValueOrDefault("chatId");
        var body = payload?.GetValueOrDefault("body")
            ?? payload?.GetValueOrDefault("message")
            ?? payload?.GetValueOrDefault("text");
        var replyToMessageId = payload?.GetValueOrDefault("replyToMessageId")
            ?? payload?.GetValueOrDefault("reply_to_message_id");

        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(body))
            return Task.FromResult("WhatsApp action is missing destination or body payload fields.");

        return ExecuteChannelSendAsync(
            services,
            "whatsapp",
            chatId.Trim(),
            body.Trim(),
            replyToMessageId,
            ct);
    }

    private async Task<string> ExecuteChannelSendAsync(
        IServiceProvider services,
        string channelId,
        string chatId,
        string text,
        string? replyToMessageId,
        CancellationToken ct)
    {
        var registry = services.GetRequiredService<IChannelRegistry>();
        var sendResult = await registry.SendToChannelAsync(
            channelId,
            new OutboundMessage(chatId, text, replyToMessageId),
            ct);
        if (!sendResult.Success)
            throw new InvalidOperationException(sendResult.Error ?? $"Failed to send message via '{channelId}'.");

        return $"Message sent via {channelId} to {chatId}. MessageId={sendResult.MessageId ?? "n/a"}";
    }

    private Task<string> ExecuteShellFromActionAsync(
        PendingAction action, IServiceProvider services, CancellationToken ct)
    {
        // Will execute shell command
        return Task.FromResult($"Would execute: {action.Payload?.GetValueOrDefault("command", "unknown")}");
    }

    private Task<string> ExecuteSystemChangeFromActionAsync(
        PendingAction action, IServiceProvider services, CancellationToken ct)
    {
        // Will apply system change
        return Task.FromResult($"Applied system change: {action.Description ?? "No description"}");
    }

    private async Task<string> ExecuteSkillToolFromActionAsync(
        KanbanTask task,
        PendingAction action,
        IServiceProvider services,
        CancellationToken ct)
    {
        var payload = action.Payload;
        if (payload == null ||
            !payload.TryGetValue("toolName", out var toolName) ||
            string.IsNullOrWhiteSpace(toolName))
        {
            throw new InvalidOperationException("Approved tool action missing payload.toolName");
        }

        var arguments = payload.TryGetValue("arguments", out var args) && !string.IsNullOrWhiteSpace(args)
            ? args
            : "{}";

        Guid? contextConversationId = task.ConversationId ?? ParseGuid(payload, "conversationId");
        Guid? contextTaskId = task.Id;
        Guid? contextUserId = task.UserId ?? ParseGuid(payload, "userId");

        var skillRegistry = services.GetRequiredService<ISkillRegistry>();
        var result = await skillRegistry.ExecuteToolAsync(
            toolName,
            arguments,
            contextConversationId,
            contextTaskId,
            contextUserId,
            ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Tool execution failed: {result.Error ?? "unknown error"}");
        }

        return string.IsNullOrWhiteSpace(result.Result)
            ? $"Executed approved tool call: {toolName}"
            : result.Result!;
    }

    private static Guid? ParseGuid(Dictionary<string, string> payload, string key)
    {
        if (payload.TryGetValue(key, out var value) && Guid.TryParse(value, out var parsed))
            return parsed;
        return null;
    }
}
