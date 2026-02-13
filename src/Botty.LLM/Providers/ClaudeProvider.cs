using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.LLM.Providers;

/// <summary>
/// Claude LLM provider using the Anthropic SDK.
/// </summary>
public class ClaudeProvider : ILlmProvider
{
    private readonly AnthropicClient _client;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeProvider> _logger;

    public ClaudeProvider(
        IOptions<ClaudeOptions> options,
        ILogger<ClaudeProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        
        _client = new AnthropicClient(_options.ApiKey);
    }

    public string Name => "Claude";

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var model = request.Parameters.Model ?? _options.DefaultModel;
        
        _logger.LogDebug("Sending request to Claude ({Model})", model);

        try
        {
            // Build the messages
            var messages = BuildMessages(request);

            // Build the request
            var messageRequest = new MessageParameters
            {
                Model = model,
                MaxTokens = request.Parameters.MaxTokens,
                Temperature = (decimal)request.Parameters.Temperature,
                System = [new SystemMessage(BuildSystemPrompt(request))],
                Messages = messages
            };

            if (request.AvailableTools is { Count: > 0 } tools)
            {
                messageRequest.Tools = tools.Select(MapToAnthropicTool).ToList();
            }

            var response = await _client.Messages.GetClaudeMessageAsync(messageRequest, ct);

            _logger.LogDebug("Claude response received: {StopReason}, tokens: {Input}/{Output}",
                response.StopReason, response.Usage?.InputTokens, response.Usage?.OutputTokens);

            return MapResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Claude API");
            throw;
        }
    }

    public async IAsyncEnumerable<StreamDelta> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = request.Parameters.Model ?? _options.DefaultModel;

        _logger.LogDebug("Starting streaming request to Claude ({Model})", model);

        var messages = BuildMessages(request);

        var messageRequest = new MessageParameters
        {
            Model = model,
            MaxTokens = request.Parameters.MaxTokens,
            Temperature = (decimal)request.Parameters.Temperature,
            System = [new SystemMessage(BuildSystemPrompt(request))],
            Messages = messages,
            Stream = true
        };

        if (request.AvailableTools is { Count: > 0 } tools)
        {
            messageRequest.Tools = tools.Select(MapToAnthropicTool).ToList();
        }

        // Track current tool call being accumulated across content_block_delta events
        string? currentToolId = null;
        string? currentToolName = null;
        var toolInputJson = new StringBuilder();
        string? finishReason = null;
        TokenUsage? usage = null;

        await foreach (var streamEvent in _client.Messages.StreamClaudeMessageAsync(messageRequest, ct))
        {
            // content_block_start: signals beginning of a text or tool_use block
            if (streamEvent.Type == "content_block_start" && streamEvent.ContentBlock != null)
            {
                var block = streamEvent.ContentBlock;
                if (block.Type == "tool_use")
                {
                    currentToolId = block.Id;
                    currentToolName = block.Name;
                    toolInputJson.Clear();
                }
            }
            // content_block_delta: carries text or partial tool JSON
            else if (streamEvent.Type == "content_block_delta" && streamEvent.Delta != null)
            {
                var delta = streamEvent.Delta;
                if (!string.IsNullOrEmpty(delta.Text))
                {
                    yield return new StreamDelta { Type = "text", Text = delta.Text };
                }
                if (!string.IsNullOrEmpty(delta.PartialJson))
                {
                    toolInputJson.Append(delta.PartialJson);
                }
            }
            // content_block_stop: if we were accumulating a tool call, emit it
            else if (streamEvent.Type == "content_block_stop")
            {
                if (currentToolId != null && currentToolName != null)
                {
                    yield return new StreamDelta
                    {
                        Type = "tool_use",
                        ToolCall = new LlmToolCall
                        {
                            Id = currentToolId,
                            Name = currentToolName,
                            Arguments = toolInputJson.ToString()
                        }
                    };
                    currentToolId = null;
                    currentToolName = null;
                    toolInputJson.Clear();
                }
            }
            // message_delta: carries stop reason and usage
            else if (streamEvent.Type == "message_delta" && streamEvent.Delta != null)
            {
                finishReason = streamEvent.Delta.StopReason;
                if (streamEvent.Delta.Usage != null)
                {
                    usage = new TokenUsage
                    {
                        CompletionTokens = streamEvent.Delta.Usage.OutputTokens
                    };
                }
            }
            // message_start: capture input token usage
            else if (streamEvent.Type == "message_start" && streamEvent.StreamStartMessage?.Usage != null)
            {
                usage ??= new TokenUsage();
                usage.PromptTokens = streamEvent.StreamStartMessage.Usage.InputTokens;
            }
        }

        // Emit final done delta
        yield return new StreamDelta
        {
            Type = "done",
            FinishReason = finishReason,
            Usage = usage
        };
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        // Claude doesn't have native embedding support
        // Use the IEmbeddingProvider interface instead
        throw new NotSupportedException("Claude does not support embeddings. Use IEmbeddingProvider.");
    }

    private static List<Anthropic.SDK.Messaging.Message> BuildMessages(LlmRequest request)
    {
        return request.Messages.Select(m =>
        {
            var role = m.Role.ToLowerInvariant() switch
            {
                "user" => RoleType.User,
                "assistant" => RoleType.Assistant,
                _ => RoleType.User
            };
            var content = BuildMessageContent(m);
            return new Anthropic.SDK.Messaging.Message { Role = role, Content = content };
        }).ToList();
    }

    private static List<ContentBase> BuildMessageContent(LlmMessage m)
    {
        if (m.ToolResults is { Count: > 0 } results)
        {
            return results.Select(r => (ContentBase)new ToolResultContent
            {
                ToolUseId = r.ToolUseId,
                Content = [new TextContent { Text = r.Content }]
            }).ToList();
        }

        var blocks = new List<ContentBase>();
        if (!string.IsNullOrEmpty(m.Content))
        {
            blocks.Add(new TextContent { Text = m.Content });
        }
        if (m.ToolCalls is { Count: > 0 } calls)
        {
            foreach (var tc in calls)
            {
                var input = JsonNode.Parse(tc.Arguments)?.AsObject()
                    ?? new JsonObject();
                blocks.Add(new ToolUseContent
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    Input = input
                });
            }
        }
        if (blocks.Count == 0)
        {
            blocks.Add(new TextContent { Text = string.Empty });
        }
        return blocks;
    }

    private static Anthropic.SDK.Common.Tool MapToAnthropicTool(LlmTool tool)
    {
        return new Function(tool.Name, tool.Description, tool.ParametersSchema);
    }

    private static string BuildSystemPrompt(LlmRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(request.SystemPrompt);

        if (!string.IsNullOrWhiteSpace(request.MemoryPack))
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(request.MemoryPack);
        }

        return sb.ToString();
    }

    private static LlmResponse MapResponse(MessageResponse response)
    {
        var content = new StringBuilder();
        var toolCalls = new List<LlmToolCall>();

        foreach (var block in response.Content)
        {
            if (block is TextContent textContent)
            {
                content.Append(textContent.Text);
            }
            else if (block is ToolUseContent toolUse)
            {
                toolCalls.Add(new LlmToolCall
                {
                    Id = toolUse.Id,
                    Name = toolUse.Name,
                    Arguments = JsonSerializer.Serialize(toolUse.Input)
                });
            }
        }

        return new LlmResponse
        {
            Content = content.ToString(),
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            FinishReason = response.StopReason,
            Usage = new TokenUsage
            {
                PromptTokens = response.Usage?.InputTokens ?? 0,
                CompletionTokens = response.Usage?.OutputTokens ?? 0
            }
        };
    }
}

/// <summary>
/// Configuration options for Claude provider.
/// </summary>
public class ClaudeOptions
{
    /// <summary>
    /// Anthropic API key.
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// Default model to use.
    /// </summary>
    public string DefaultModel { get; set; } = "claude-sonnet-4-20250514";

    /// <summary>
    /// Default max tokens.
    /// </summary>
    public int DefaultMaxTokens { get; set; } = 4096;
}
