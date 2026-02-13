namespace Botty.Core.Interfaces;

/// <summary>
/// Alias for LlmMessage used in higher-level APIs.
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// Role of the message sender (user, assistant, system).
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Content of the message.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Name of the sender (optional).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Tool calls from an assistant message.
    /// </summary>
    public List<LlmToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// Tool results for a user message.
    /// </summary>
    public List<LlmToolResult>? ToolResults { get; set; }

    /// <summary>
    /// Converts to an LlmMessage.
    /// </summary>
    public LlmMessage ToLlmMessage() => new()
    {
        Role = Role,
        Content = Content,
        Name = Name,
        ToolCalls = ToolCalls,
        ToolResults = ToolResults
    };
}

/// <summary>
/// Interface for LLM providers (Claude, GPT, etc.).
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Name of the LLM provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Completes a chat request.
    /// </summary>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);

    /// <summary>
    /// Generates an embedding vector for text.
    /// </summary>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Streams a completion response as structured deltas.
    /// </summary>
    IAsyncEnumerable<StreamDelta> StreamCompleteAsync(LlmRequest request, CancellationToken ct = default);
}

/// <summary>
/// A structured delta event from a streaming LLM response.
/// </summary>
public class StreamDelta
{
    /// <summary>
    /// Type of delta: "text", "tool_use", or "done".
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Text content (for "text" deltas).
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Completed tool call (for "tool_use" deltas).
    /// </summary>
    public LlmToolCall? ToolCall { get; set; }

    /// <summary>
    /// Token usage (for "done" deltas).
    /// </summary>
    public TokenUsage? Usage { get; set; }

    /// <summary>
    /// Finish reason (for "done" deltas).
    /// </summary>
    public string? FinishReason { get; set; }
}

/// <summary>
/// Request to an LLM.
/// </summary>
public class LlmRequest
{
    /// <summary>
    /// System prompt including Soul configuration.
    /// </summary>
    public required string SystemPrompt { get; set; }

    /// <summary>
    /// Memory pack to include in context.
    /// </summary>
    public string? MemoryPack { get; set; }

    /// <summary>
    /// Conversation messages.
    /// </summary>
    public List<LlmMessage> Messages { get; set; } = [];

    /// <summary>
    /// Available tools/functions.
    /// </summary>
    public List<LlmTool>? AvailableTools { get; set; }

    /// <summary>
    /// LLM parameters (temperature, max tokens, etc.).
    /// </summary>
    public LlmParameters Parameters { get; set; } = new();
}

/// <summary>
/// A message in an LLM conversation.
/// </summary>
public class LlmMessage
{
    /// <summary>
    /// Role of the message sender.
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Content of the message (text). Optional when ToolCalls or ToolResults are present.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Name of the sender (for multi-party conversations).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Tool calls from an assistant message.
    /// </summary>
    public List<LlmToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// Tool results for a user message (responses to tool_use).
    /// </summary>
    public List<LlmToolResult>? ToolResults { get; set; }
}

/// <summary>
/// A tool result to send back to the LLM after executing a tool call.
/// </summary>
public class LlmToolResult
{
    /// <summary>
    /// Id of the tool use this result is for.
    /// </summary>
    public required string ToolUseId { get; set; }

    /// <summary>
    /// Result content (e.g. JSON or text).
    /// </summary>
    public required string Content { get; set; }
}

/// <summary>
/// A tool/function available to the LLM.
/// </summary>
public class LlmTool
{
    /// <summary>
    /// Name of the tool.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Description of what the tool does.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// JSON schema for the tool's parameters.
    /// </summary>
    public required string ParametersSchema { get; set; }
}

/// <summary>
/// Parameters for LLM completion.
/// </summary>
public class LlmParameters
{
    /// <summary>
    /// Temperature for sampling (0.0 to 1.0).
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Maximum tokens to generate.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Model to use (if provider supports multiple).
    /// </summary>
    public string? Model { get; set; }
}

/// <summary>
/// Response from an LLM.
/// </summary>
public class LlmResponse
{
    /// <summary>
    /// Generated content.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Tool calls requested by the LLM (if any).
    /// </summary>
    public List<LlmToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// Token usage information.
    /// </summary>
    public TokenUsage? Usage { get; set; }

    /// <summary>
    /// Finish reason.
    /// </summary>
    public string? FinishReason { get; set; }
}

/// <summary>
/// A tool call requested by the LLM.
/// </summary>
public class LlmToolCall
{
    /// <summary>
    /// ID of the tool call.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Name of the tool to call.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Arguments for the tool call (JSON).
    /// </summary>
    public required string Arguments { get; set; }
}

/// <summary>
/// Token usage information.
/// </summary>
public class TokenUsage
{
    /// <summary>
    /// Number of tokens in the prompt.
    /// </summary>
    public int PromptTokens { get; set; }

    /// <summary>
    /// Number of tokens in the completion.
    /// </summary>
    public int CompletionTokens { get; set; }

    /// <summary>
    /// Total tokens used.
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;
}
