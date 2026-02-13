using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.AspNetCore.Mvc;

using MemoryEntity = Botty.Core.Models.Memory;

namespace Botty.Api.Controllers;

/// <summary>
/// API endpoints for memory management and trust layer commands.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MemoryController : ControllerBase
{
    private static readonly Guid DefaultAdminUserId = new("00000000-0000-0000-0000-000000000001");

    private readonly IMemoryTrustService _trustService;
    private readonly IMemoryIngestionService _ingestionService;
    private readonly IMemoryRetrievalService _retrievalService;
    private readonly IMemoryRepository _memoryRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MemoryController> _logger;

    public MemoryController(
        IMemoryTrustService trustService,
        IMemoryIngestionService ingestionService,
        IMemoryRetrievalService retrievalService,
        IMemoryRepository memoryRepository,
        IConfiguration configuration,
        ILogger<MemoryController> logger)
    {
        _trustService = trustService;
        _ingestionService = ingestionService;
        _retrievalService = retrievalService;
        _memoryRepository = memoryRepository;
        _configuration = configuration;
        _logger = logger;
    }

    private Guid GetDefaultAdminUserId()
    {
        var value = _configuration["Conversation:DefaultAdminUserId"];
        return Guid.TryParse(value, out var id) ? id : DefaultAdminUserId;
    }

    /// <summary>
    /// Search or list memories (admin UI). Uses default admin user when no userId provided.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchMemories(
        [FromQuery] string? query,
        [FromQuery] string? type,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var userId = GetDefaultAdminUserId();
        IEnumerable<MemoryEntity> memories;
        if (string.IsNullOrWhiteSpace(query))
        {
            memories = await _trustService.GetRememberedMemoriesAsync(userId, ct);
        }
        else
        {
            memories = await _trustService.SearchMemoriesAsync(userId, query!, ct);
        }

        var list = memories.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(type) && type != "All")
        {
            if (Enum.TryParse<MemoryType>(type, true, out var memoryType))
                list = list.Where(m => m.Type == memoryType);
        }
        if (limit is > 0)
            list = list.Take(limit.Value);

        var dtos = list.Select(m => MapToDto(m)).ToList();
        return Ok(new { memories = dtos });
    }

    private static MemoryDto MapToDto(MemoryEntity m)
    {
        return new MemoryDto
        {
            Id = m.Id,
            Content = m.Content,
            Type = m.Type.ToString(),
            Sensitivity = m.Sensitivity.ToString(),
            Confidence = m.Confidence,
            Source = m.Source,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt
        };
    }

    /// <summary>
    /// Get memory stats for admin UI (default admin user).
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var userId = GetDefaultAdminUserId();
        var memories = (await _trustService.GetRememberedMemoriesAsync(userId, ct)).ToList();
        var byType = memories
            .GroupBy(m => m.Type.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        return Ok(new { totalCount = memories.Count, byType });
    }

    /// <summary>
    /// Get a single memory by ID (admin UI).
    /// </summary>
    [HttpGet("{memoryId:guid}")]
    public async Task<IActionResult> GetMemory(Guid memoryId, CancellationToken ct)
    {
        var memory = await _memoryRepository.GetByIdAsync(memoryId, ct);
        if (memory == null)
            return NotFound();
        return Ok(MapToDto(memory));
    }

    /// <summary>
    /// Gets all memories for a user (Trust Layer: "What do you remember about me?").
    /// </summary>
    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> GetUserMemories(Guid userId, CancellationToken ct)
    {
        var memories = await _trustService.GetRememberedMemoriesAsync(userId, ct);
        return Ok(memories.Select(MapToDto));
    }

    /// <summary>
    /// Searches memories (Trust Layer: "What do you remember about X?").
    /// </summary>
    [HttpGet("user/{userId:guid}/search")]
    public async Task<IActionResult> SearchMemories(Guid userId, [FromQuery] string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("Query is required");
        }

        var memories = await _trustService.SearchMemoriesAsync(userId, query, ct);
        return Ok(memories.Select(MapToDto));
    }

    /// <summary>
    /// Retrieves a memory pack for LLM injection.
    /// </summary>
    [HttpPost("user/{userId:guid}/pack")]
    public async Task<IActionResult> GetMemoryPack(Guid userId, [FromBody] MemoryPackRequest request, CancellationToken ct)
    {
        var pack = await _retrievalService.RetrieveMemoryPackAsync(
            userId,
            request.Query ?? "",
            new MemoryRetrievalOptions
            {
                MaxMemories = request.MaxMemories ?? 20,
                TopPreferences = request.TopPreferences ?? 5,
                ActiveProjects = request.ActiveProjects ?? 3,
                SemanticSearchResults = request.SemanticResults ?? 10
            },
            ct);

        return Ok(new MemoryPackResponse
        {
            FormattedText = pack.FormattedText,
            Count = pack.Count,
            Memories = pack.Memories.Select(MapToDto).ToList()
        });
    }

    /// <summary>
    /// Adds a manual memory (Trust Layer: Manual entry).
    /// </summary>
    [HttpPost("user/{userId:guid}")]
    public async Task<IActionResult> AddMemory(Guid userId, [FromBody] AddMemoryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Content is required");
        }

        if (!Enum.TryParse<MemoryType>(request.Type, true, out var memoryType))
        {
            memoryType = MemoryType.Fact;
        }

        var memory = await _ingestionService.IngestManualMemoryAsync(
            userId, request.Content, memoryType, ct);

        return CreatedAtAction(nameof(GetUserMemories), new { userId }, MapToDto(memory));
    }

    /// <summary>
    /// Forgets a memory by ID (Trust Layer: "Forget this").
    /// </summary>
    [HttpDelete("{memoryId:guid}")]
    public async Task<IActionResult> ForgetMemory(Guid memoryId, CancellationToken ct)
    {
        var result = await _trustService.ForgetMemoryByIdAsync(memoryId, ct);
        
        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// Forgets memories matching a query (Trust Layer: "Forget X").
    /// </summary>
    [HttpDelete("user/{userId:guid}/forget")]
    public async Task<IActionResult> ForgetByQuery(Guid userId, [FromQuery] string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("Query is required");
        }

        var result = await _trustService.ForgetMemoryAsync(userId, query, ct);
        
        if (!result)
        {
            return NotFound("No matching memory found");
        }

        return NoContent();
    }

    /// <summary>
    /// Corrects a memory (Trust Layer: "Actually, it's Y not X").
    /// </summary>
    [HttpPut("user/{userId:guid}/correct")]
    public async Task<IActionResult> CorrectMemory(Guid userId, [FromBody] CorrectMemoryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OriginalQuery) || string.IsNullOrWhiteSpace(request.Correction))
        {
            return BadRequest("Both originalQuery and correction are required");
        }

        var memory = await _trustService.CorrectMemoryAsync(userId, request.OriginalQuery, request.Correction, ct);
        
        if (memory == null)
        {
            return NotFound("No matching memory found");
        }

        return Ok(new MemoryDto
        {
            Id = memory.Id,
            Content = memory.Content,
            Type = memory.Type.ToString(),
            Sensitivity = memory.Sensitivity.ToString(),
            Confidence = memory.Confidence,
            CreatedAt = memory.CreatedAt,
            UpdatedAt = memory.UpdatedAt
        });
    }

    /// <summary>
    /// Sets session privacy for a conversation (Trust Layer: "Don't store this chat").
    /// </summary>
    [HttpPut("conversation/{conversationId:guid}/privacy")]
    public async Task<IActionResult> SetSessionPrivacy(Guid conversationId, [FromBody] SessionPrivacyRequest request, CancellationToken ct)
    {
        await _trustService.SetSessionPrivacyAsync(conversationId, request.StoreMemories, ct);
        return NoContent();
    }
}

#region DTOs

public class MemoryDto
{
    public Guid Id { get; set; }
    public required string Content { get; set; }
    public required string Type { get; set; }
    public required string Sensitivity { get; set; }
    public decimal Confidence { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Stub for admin UI; not persisted.</summary>
    public int AccessCount { get; set; }
    /// <summary>Stub for admin UI; not persisted.</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];
}

public class MemoryPackRequest
{
    public string? Query { get; set; }
    public int? MaxMemories { get; set; }
    public int? TopPreferences { get; set; }
    public int? ActiveProjects { get; set; }
    public int? SemanticResults { get; set; }
}

public class MemoryPackResponse
{
    public required string FormattedText { get; set; }
    public int Count { get; set; }
    public required IList<MemoryDto> Memories { get; set; }
}

public class AddMemoryRequest
{
    public required string Content { get; set; }
    public string? Type { get; set; }
}

public class CorrectMemoryRequest
{
    public required string OriginalQuery { get; set; }
    public required string Correction { get; set; }
}

public class SessionPrivacyRequest
{
    public bool StoreMemories { get; set; }
}

#endregion
