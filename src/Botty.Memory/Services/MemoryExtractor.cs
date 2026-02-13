using System.Text.Json;
using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Memory.Services;

/// <summary>
/// Extracts durable memories from conversations using an LLM.
/// </summary>
public class MemoryExtractor : IMemoryExtractor
{
    private readonly ILlmProvider _llmProvider;
    private readonly ILogger<MemoryExtractor> _logger;

    private const string ExtractionPrompt = """
        You are a memory extraction system. Analyze the following conversation and extract 0-5 durable memories worth remembering about the user.

        ALWAYS extract when the user states them (high priority):
        - Identity: name, what to call them, pronouns (e.g. "User's name is John", "User prefers to be called Alex")
        - Core facts: job, location, family

        Also extract when present:
        - Preferences (likes, dislikes, habits)
        - Ongoing projects or goals
        - Relationships (people the user mentions)
        - Important events or episodes

        Do NOT extract:
        - Transient or task-only information (e.g. a one-off request like "weather in X" unless the user expresses an ongoing interest)
        - Things the assistant said (only user information)
        - Redundant or obvious information

        For each memory, provide:
        - content: The memory in a concise statement (e.g., "User's name is Sarah")
        - type: One of [preference, project, artifact, episode, fact, relationship]
        - sensitivity: One of [public, private, sensitive]
        - ttl_days: Number of days before this should expire (null for permanent)
        - confidence: How confident you are this is a durable memory (0.0-1.0). Use 0.9+ for clear identity/fact statements.

        Respond with a JSON array of memories. If no memories are worth extracting, return an empty array.

        Example response:
        [
            {"content": "User's name is John", "type": "fact", "sensitivity": "public", "ttl_days": null, "confidence": 0.95},
            {"content": "User works as a software engineer at Google", "type": "fact", "sensitivity": "private", "ttl_days": null, "confidence": 0.95},
            {"content": "User is working on a home automation project", "type": "project", "sensitivity": "private", "ttl_days": 90, "confidence": 0.8}
        ]

        Conversation:
        """;

    public MemoryExtractor(ILlmProvider llmProvider, ILogger<MemoryExtractor> logger)
    {
        _llmProvider = llmProvider;
        _logger = logger;
    }

    public async Task<IList<ExtractedMemory>> ExtractMemoriesAsync(
        Conversation conversation,
        CancellationToken ct = default)
    {
        if (!conversation.StoreMemories)
        {
            _logger.LogDebug("Skipping memory extraction for conversation {Id} - StoreMemories is false", 
                conversation.Id);
            return [];
        }

        if (conversation.Messages.Count == 0)
        {
            return [];
        }

        // Format conversation for extraction
        var conversationText = FormatConversation(conversation);
        var fullPrompt = ExtractionPrompt + "\n" + conversationText;

        try
        {
            var request = new LlmRequest
            {
                SystemPrompt = "You are a memory extraction system. Always respond with valid JSON.",
                Messages =
                [
                    new LlmMessage { Role = "user", Content = fullPrompt }
                ],
                Parameters = new LlmParameters
                {
                    Temperature = 0.3, // Lower temperature for more consistent extraction
                    MaxTokens = 1024
                }
            };

            var response = await _llmProvider.CompleteAsync(request, ct);
            var memories = ParseExtractionResponse(response.Content);

            _logger.LogInformation("Extracted {Count} memories from conversation {Id}",
                memories.Count, conversation.Id);

            return memories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract memories from conversation {Id}", conversation.Id);
            return [];
        }
    }

    private static string FormatConversation(Conversation conversation)
    {
        var lines = conversation.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m =>
            {
                var role = m.Role switch
                {
                    MessageRole.User => "User",
                    MessageRole.Assistant => "Assistant",
                    MessageRole.ThirdParty => m.SenderName ?? "Other",
                    _ => "System"
                };
                return $"{role}: {m.Content}";
            });

        return string.Join("\n", lines);
    }

    private IList<ExtractedMemory> ParseExtractionResponse(string response)
    {
        try
        {
            // Try to find JSON array in the response
            var startIndex = response.IndexOf('[');
            var endIndex = response.LastIndexOf(']');
            
            if (startIndex < 0 || endIndex < startIndex)
            {
                _logger.LogWarning("No JSON array found in extraction response");
                return [];
            }

            var jsonArray = response.Substring(startIndex, endIndex - startIndex + 1);
            var rawMemories = JsonSerializer.Deserialize<List<RawExtractedMemory>>(jsonArray, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (rawMemories == null)
            {
                return [];
            }

            return rawMemories
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => new ExtractedMemory
                {
                    Content = m.Content!.Trim(),
                    Type = ParseMemoryType(m.Type),
                    Sensitivity = ParseSensitivity(m.Sensitivity),
                    TtlDays = m.TtlDays,
                    RawConfidence = Math.Clamp(m.Confidence ?? 1.0m, 0m, 1m)
                })
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse extraction response as JSON");
            return [];
        }
    }

    private static MemoryType ParseMemoryType(string? type) => type?.ToLowerInvariant() switch
    {
        "preference" => MemoryType.Preference,
        "project" => MemoryType.Project,
        "artifact" => MemoryType.Artifact,
        "episode" => MemoryType.Episode,
        "fact" => MemoryType.Fact,
        "relationship" => MemoryType.Relationship,
        _ => MemoryType.Fact
    };

    private static MemorySensitivity ParseSensitivity(string? sensitivity) => sensitivity?.ToLowerInvariant() switch
    {
        "public" => MemorySensitivity.Public,
        "private" => MemorySensitivity.Private,
        "sensitive" => MemorySensitivity.Sensitive,
        _ => MemorySensitivity.Private
    };

    private class RawExtractedMemory
    {
        public string? Content { get; set; }
        public string? Type { get; set; }
        public string? Sensitivity { get; set; }
        public int? TtlDays { get; set; }
        public decimal? Confidence { get; set; }
    }
}
