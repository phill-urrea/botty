using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.LLM.Services;

/// <summary>
/// Orchestrates conversations with the LLM, combining Soul configuration and memory.
/// </summary>
public interface IConversationOrchestrator
{
    /// <summary>
    /// Sends a message and gets a response, with full context management.
    /// </summary>
    Task<ConversationResponse> ChatAsync(
        ConversationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Streams a response for a message.
    /// </summary>
    IAsyncEnumerable<string> StreamChatAsync(
        ConversationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Processes a conversation for memory extraction.
    /// </summary>
    Task ProcessConversationForMemoryAsync(
        IEnumerable<ChatMessage> messages,
        Guid? userId = null,
        string? conversationId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Request for a conversation.
/// </summary>
public class ConversationRequest
{
    /// <summary>
    /// The conversation history (including the latest user message).
    /// </summary>
    public required List<ChatMessage> Messages { get; set; }

    /// <summary>
    /// Optional conversation ID for tracking.
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Optional user ID for personalization.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Whether to include memory context.
    /// </summary>
    public bool IncludeMemory { get; set; } = true;

    /// <summary>
    /// Whether to extract memories from this conversation.
    /// </summary>
    public bool ExtractMemories { get; set; } = true;

    /// <summary>
    /// Custom context to include in the prompt.
    /// </summary>
    public string? AdditionalContext { get; set; }

    /// <summary>
    /// Available tools/skills for this conversation.
    /// </summary>
    public List<LlmTool>? AvailableTools { get; set; }

    /// <summary>
    /// LLM parameters override.
    /// </summary>
    public LlmParameters? Parameters { get; set; }
}

/// <summary>
/// Response from a conversation.
/// </summary>
public class ConversationResponse
{
    /// <summary>
    /// The assistant's response content.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Any tool calls requested by the assistant.
    /// </summary>
    public List<LlmToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// Reason for stopping.
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// Token usage for this response.
    /// </summary>
    public TokenUsage? Usage { get; set; }

    /// <summary>
    /// Whether memory extraction was triggered.
    /// </summary>
    public bool MemoryExtractionTriggered { get; set; }

    /// <summary>
    /// Number of memories injected into context.
    /// </summary>
    public int MemoriesInjected { get; set; }
}

/// <summary>
/// Configuration options for the conversation orchestrator.
/// </summary>
public class ConversationOptions
{
    /// <summary>
    /// Minimum number of turns before memory extraction.
    /// </summary>
    public int MemoryExtractionMinTurns { get; set; } = 3;

    /// <summary>
    /// Maximum tokens to allocate for memory pack.
    /// </summary>
    public int MaxMemoryTokens { get; set; } = 500;

    /// <summary>
    /// Default model to use.
    /// </summary>
    public string DefaultModel { get; set; } = "claude-sonnet-4-20250514";

    /// <summary>
    /// Default max tokens for responses.
    /// </summary>
    public int DefaultMaxTokens { get; set; } = 4096;

    /// <summary>
    /// Default temperature.
    /// </summary>
    public float DefaultTemperature { get; set; } = 0.7f;

    /// <summary>
    /// Default user ID to use when none is provided.
    /// </summary>
    public Guid DefaultUserId { get; set; } = Guid.Empty;
}

/// <summary>
/// Implementation of IConversationOrchestrator.
/// </summary>
public class ConversationOrchestrator : IConversationOrchestrator
{
    private const string AskUserIdentityDirective =
        "## User identity\nYou do not yet know this user's name or how to address them. When it fits naturally (e.g. at the start of the conversation or when saying hello), ask them (e.g. \"What should I call you?\" or \"May I ask your name?\"). Do not ask repeatedly if they prefer not to share.";

    private readonly ILlmProvider _llmProvider;
    private readonly ISoulService _soulService;
    private readonly IMemoryRetrievalService? _memoryRetrievalService;
    private readonly IMemoryIngestionService? _memoryIngestionService;
    private readonly ConversationOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConversationOrchestrator> _logger;

    public ConversationOrchestrator(
        ILlmProvider llmProvider,
        ISoulService soulService,
        IOptions<ConversationOptions> options,
        ILogger<ConversationOrchestrator> logger,
        IServiceScopeFactory scopeFactory,
        IMemoryRetrievalService? memoryRetrievalService = null,
        IMemoryIngestionService? memoryIngestionService = null)
    {
        _llmProvider = llmProvider;
        _soulService = soulService;
        _scopeFactory = scopeFactory;
        _memoryRetrievalService = memoryRetrievalService;
        _memoryIngestionService = memoryIngestionService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ConversationResponse> ChatAsync(
        ConversationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Processing conversation request with {MessageCount} messages", 
            request.Messages.Count);

        var userId = request.UserId ?? _options.DefaultUserId;

        // Get Soul configuration
        var soul = await _soulService.GetCurrentAsync(ct);

        // Get memory pack if enabled
        string memoryPack = string.Empty;
        var memoriesInjected = 0;
        MemoryPack? memoryResult = null;

        if (request.IncludeMemory && _memoryRetrievalService != null && userId != Guid.Empty)
        {
            var lastUserMessage = request.Messages
                .LastOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                ?.Content ?? string.Empty;

            memoryResult = await _memoryRetrievalService.RetrieveMemoryPackAsync(
                userId,
                lastUserMessage,
                new MemoryRetrievalOptions
                {
                    MaxMemories = 20
                },
                ct);

            memoryPack = memoryResult.FormattedText;
            memoriesInjected = memoryResult.Count;

            _logger.LogDebug("Injected {MemoryCount} memories into context", memoriesInjected);
        }

        // Build system prompt from Soul + Memory Pack
        var systemPrompt = _soulService.GenerateSystemPrompt(soul, memoryPack);

        // If we have no memory of who the user is, ask when appropriate
        if (memoryResult != null && !HasIdentityMemory(memoryResult.Memories))
        {
            systemPrompt = $"{systemPrompt}\n\n{AskUserIdentityDirective}";
        }

        // Add additional context if provided
        if (!string.IsNullOrWhiteSpace(request.AdditionalContext))
        {
            systemPrompt = $"{systemPrompt}\n\n## Additional Context\n{request.AdditionalContext}";
        }

        // Build LLM request - convert ChatMessage to LlmMessage
        var llmMessages = request.Messages.Select(m => m.ToLlmMessage()).ToList();

        var llmRequest = new LlmRequest
        {
            SystemPrompt = systemPrompt,
            Messages = llmMessages,
            MemoryPack = memoryPack,
            AvailableTools = request.AvailableTools,
            Parameters = request.Parameters ?? new LlmParameters
            {
                Model = _options.DefaultModel,
                MaxTokens = _options.DefaultMaxTokens,
                Temperature = _options.DefaultTemperature
            }
        };

        // Get response from LLM
        var llmResponse = await _llmProvider.CompleteAsync(llmRequest, ct);

        // Check if we should extract memories
        var shouldExtractMemories = request.ExtractMemories &&
                                   _memoryIngestionService != null &&
                                   userId != Guid.Empty &&
                                   request.Messages.Count >= _options.MemoryExtractionMinTurns * 2;

        if (shouldExtractMemories)
        {
            // Fire and forget memory extraction (don't block the response)
            _ = ProcessConversationForMemoryAsync(
                request.Messages, 
                userId, 
                request.ConversationId, 
                CancellationToken.None);
        }

        return new ConversationResponse
        {
            Content = llmResponse.Content,
            ToolCalls = llmResponse.ToolCalls,
            FinishReason = llmResponse.FinishReason,
            Usage = llmResponse.Usage,
            MemoryExtractionTriggered = shouldExtractMemories,
            MemoriesInjected = memoriesInjected
        };
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        ConversationRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogDebug("Starting streaming conversation with {MessageCount} messages",
            request.Messages.Count);

        var userId = request.UserId ?? _options.DefaultUserId;

        // Get Soul configuration
        var soul = await _soulService.GetCurrentAsync(ct);

        // Get memory pack if enabled
        string memoryPack = string.Empty;
        MemoryPack? memoryResult = null;

        if (request.IncludeMemory && _memoryRetrievalService != null && userId != Guid.Empty)
        {
            var lastUserMessage = request.Messages
                .LastOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                ?.Content ?? string.Empty;

            memoryResult = await _memoryRetrievalService.RetrieveMemoryPackAsync(
                userId,
                lastUserMessage,
                new MemoryRetrievalOptions { MaxMemories = 20 },
                ct);

            memoryPack = memoryResult.FormattedText;
        }

        // Build system prompt from Soul + Memory Pack
        var systemPrompt = _soulService.GenerateSystemPrompt(soul, memoryPack);

        // If we have no memory of who the user is, ask when appropriate
        if (memoryResult != null && !HasIdentityMemory(memoryResult.Memories))
        {
            systemPrompt = $"{systemPrompt}\n\n{AskUserIdentityDirective}";
        }

        // Add additional context if provided
        if (!string.IsNullOrWhiteSpace(request.AdditionalContext))
        {
            systemPrompt = $"{systemPrompt}\n\n## Additional Context\n{request.AdditionalContext}";
        }

        // Build LLM request - convert ChatMessage to LlmMessage
        var llmMessages = request.Messages.Select(m => m.ToLlmMessage()).ToList();

        var llmRequest = new LlmRequest
        {
            SystemPrompt = systemPrompt,
            Messages = llmMessages,
            MemoryPack = memoryPack,
            AvailableTools = request.AvailableTools,
            Parameters = request.Parameters ?? new LlmParameters
            {
                Model = _options.DefaultModel,
                MaxTokens = _options.DefaultMaxTokens,
                Temperature = _options.DefaultTemperature
            }
        };

        // Stream response
        await foreach (var chunk in _llmProvider.StreamCompleteAsync(llmRequest, ct))
        {
            yield return chunk;
        }

        // Trigger memory extraction if applicable
        if (request.ExtractMemories && 
            _memoryIngestionService != null &&
            userId != Guid.Empty &&
            request.Messages.Count >= _options.MemoryExtractionMinTurns * 2)
        {
            _ = ProcessConversationForMemoryAsync(
                request.Messages, 
                userId, 
                request.ConversationId, 
                CancellationToken.None);
        }
    }

    public async Task ProcessConversationForMemoryAsync(
        IEnumerable<ChatMessage> messages,
        Guid? userId = null,
        string? conversationId = null,
        CancellationToken ct = default)
    {
        if (_memoryIngestionService == null)
        {
            _logger.LogWarning("Memory ingestion service not available");
            return;
        }

        var effectiveUserId = userId ?? _options.DefaultUserId;
        if (effectiveUserId == Guid.Empty)
        {
            _logger.LogWarning("Cannot extract memories without a user ID");
            return;
        }

        try
        {
            _logger.LogDebug("Processing conversation for memory extraction");

            // Convert ChatMessage list to Conversation model
            var conversation = new Conversation
            {
                Id = conversationId != null && Guid.TryParse(conversationId, out var cid) ? cid : Guid.NewGuid(),
                UserId = effectiveUserId,
                Source = "api",
                StoreMemories = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Messages = messages.Select((m, i) => new Message
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId != null && Guid.TryParse(conversationId, out var msgCid) ? msgCid : Guid.Empty,
                    Role = m.Role.ToLowerInvariant() switch
                    {
                        "user" => MessageRole.User,
                        "assistant" => MessageRole.Assistant,
                        "system" => MessageRole.System,
                        _ => MessageRole.User
                    },
                    Content = m.Content,
                    SenderName = m.Name,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-messages.Count() + i)
                }).ToList()
            };

            // Run ingestion in a new scope so it has its own DbContext (request scope is disposed after response)
            using (var scope = _scopeFactory.CreateScope())
            {
                var ingestion = scope.ServiceProvider.GetRequiredService<IMemoryIngestionService>();
                var result = await ingestion.IngestFromConversationAsync(conversation, ct);
                _logger.LogInformation(
                    "Memory extraction complete: {Count} memories created/updated",
                    result.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during memory extraction");
        }
    }

    /// <summary>
    /// Returns true if the pack contains at least one identity-related fact (name, what to call user, pronouns).
    /// </summary>
    private static bool HasIdentityMemory(IList<Memory> memories)
    {
        if (memories == null || memories.Count == 0)
            return false;

        foreach (var m in memories)
        {
            if (m.Type != MemoryType.Fact)
                continue;
            var lower = m.Content?.ToLowerInvariant() ?? "";
            if (lower.Contains("name is") || lower.Contains("call me") || lower.Contains("i'm ") ||
                lower.Contains("i am ") || lower.Contains("pronouns") || lower.Contains("prefer to be called"))
                return true;
        }
        return false;
    }
}
