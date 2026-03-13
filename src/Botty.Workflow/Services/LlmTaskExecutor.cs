using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.LLM.Services;
using Microsoft.Extensions.Logging;

namespace Botty.Workflow.Services;

/// <summary>
/// Executes a Kanban task by running an LLM conversation with tools.
/// Used by the AssistantEventLoop when a scheduled task fires with a prompt.
/// </summary>
public class LlmTaskExecutor
{
    private const int MaxToolLoopIterations = 5;

    private readonly IConversationOrchestrator _orchestrator;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<LlmTaskExecutor> _logger;

    public LlmTaskExecutor(
        IConversationOrchestrator orchestrator,
        IToolRegistry toolRegistry,
        ILogger<LlmTaskExecutor> logger)
    {
        _orchestrator = orchestrator;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Executes a task by sending its prompt to the LLM with all available tools,
    /// running the tool loop, and returning the final text result.
    /// </summary>
    public async Task<string> ExecuteAsync(KanbanTask task, CancellationToken ct = default)
    {
        var prompt = task.PendingActionData?.Payload?.GetValueOrDefault("prompt")
            ?? task.Description
            ?? task.Title;

        _logger.LogInformation("Executing LLM task {Id}: {Title} with prompt length={PromptLength}",
            task.Id, task.Title, prompt.Length);

        var tools = _toolRegistry.GetAll().SelectMany(s => s.GetTools()).ToList();

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        var request = new ConversationRequest
        {
            Messages = messages,
            UserId = task.UserId,
            IncludeMemory = true,
            ExtractMemories = false,
            AvailableTools = tools,
            AdditionalContext = $"You are executing a scheduled task: \"{task.Title}\". " +
                "Produce a complete, well-formatted response. Do not ask follow-up questions."
        };

        var response = await _orchestrator.ChatAsync(request, ct);
        var iterations = 0;

        while (response.ToolCalls is { Count: > 0 } toolCalls && iterations < MaxToolLoopIterations)
        {
            iterations++;

            _logger.LogDebug("LLM task {Id} tool loop iteration {Iteration} with {ToolCount} tool calls",
                task.Id, iterations, toolCalls.Count);

            var toolResults = new List<LlmToolResult>();
            foreach (var tc in toolCalls)
            {
                var toolResult = await _toolRegistry.ExecuteToolAsync(tc.Name, tc.Arguments ?? "{}", ct);
                if (!toolResult.Success)
                {
                    _logger.LogWarning("Tool {ToolName} failed during scheduled task {TaskId}: {Error}",
                        tc.Name, task.Id, toolResult.Error);
                }

                toolResults.Add(new LlmToolResult
                {
                    ToolUseId = tc.Id,
                    Content = toolResult.Success
                        ? (toolResult.Result ?? string.Empty)
                        : $"Error: {toolResult.Error}"
                });
            }

            request.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = response.Content,
                ToolCalls = toolCalls
            });
            request.Messages.Add(new ChatMessage
            {
                Role = "user",
                Content = string.Empty,
                ToolResults = toolResults
            });

            response = await _orchestrator.ChatAsync(request, ct);
        }

        _logger.LogInformation("LLM task {Id} completed after {Iterations} tool iterations, result length={ResultLength}",
            task.Id, iterations, response.Content.Length);

        return response.Content;
    }
}
