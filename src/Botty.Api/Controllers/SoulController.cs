using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Botty.Api.Controllers;

/// <summary>
/// API controller for managing Soul configuration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SoulController : ControllerBase
{
    private readonly ISoulService _soulService;
    private readonly ILogger<SoulController> _logger;

    public SoulController(
        ISoulService soulService,
        ILogger<SoulController> logger)
    {
        _soulService = soulService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current Soul configuration.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SoulConfigurationDto>> GetSoul(CancellationToken ct)
    {
        try
        {
            var soul = await _soulService.GetCurrentAsync(ct);

            return Ok(new SoulConfigurationDto
            {
                Name = soul.Name ?? "Botty",
                Role = soul.Role ?? "Personal AI assistant",
                PrimaryDirectives = soul.Directives,
                Tone = new ToneConfigurationDto
                {
                    CommunicationStyle = soul.Tone.CommunicationStyle,
                    HumorLevel = soul.Tone.HumorLevel,
                    Verbosity = soul.Tone.Verbosity,
                    FormalityWithOthers = soul.Tone.FormalityWithOthers
                },
                Boundaries = new BoundaryConfigurationDto
                {
                    TopicsToAvoid = soul.Boundaries.TopicsToAvoid,
                    ActionsNeverToTakeAutonomously = soul.Boundaries.NeverAutonomousActions,
                    InformationNeverToShare = soul.Boundaries.NeverShareInfo
                },
                WorkingHours = new WorkingHoursConfigurationDto
                {
                    ActiveHours = soul.WorkingHours.ActiveHours ?? string.Empty,
                    UrgentOverride = soul.WorkingHours.UrgentOverride ?? string.Empty
                },
                ResponseTemplates = soul.ResponseTemplates
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting soul configuration");
            return StatusCode(500, new { error = "Error retrieving soul configuration" });
        }
    }

    /// <summary>
    /// Gets the raw Soul.md markdown content.
    /// </summary>
    [HttpGet("markdown")]
    public async Task<ActionResult<SoulMarkdownDto>> GetSoulMarkdown(CancellationToken ct)
    {
        try
        {
            var soul = await _soulService.GetCurrentAsync(ct);

            return Ok(new SoulMarkdownDto { Content = soul.RawContent });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting soul markdown");
            return StatusCode(500, new { error = "Error retrieving soul markdown" });
        }
    }

    /// <summary>
    /// Updates the Soul.md content.
    /// </summary>
    [HttpPut("markdown")]
    public async Task<ActionResult> UpdateSoulMarkdown(
        [FromBody] SoulMarkdownDto request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Content cannot be empty");
        }

        try
        {
            await _soulService.UpdateAsync(request.Content, "api", ct);

            _logger.LogInformation("Soul.md updated successfully");

            return Ok(new { message = "Soul configuration updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating soul markdown");
            return StatusCode(500, new { error = "Error updating soul configuration" });
        }
    }

    /// <summary>
    /// Gets the generated system prompt based on current Soul configuration.
    /// </summary>
    [HttpGet("system-prompt")]
    public async Task<ActionResult<SystemPromptDto>> GetSystemPrompt(CancellationToken ct)
    {
        try
        {
            var soul = await _soulService.GetCurrentAsync(ct);
            var systemPrompt = _soulService.GenerateSystemPrompt(soul, string.Empty);

            return Ok(new SystemPromptDto { Prompt = systemPrompt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating system prompt");
            return StatusCode(500, new { error = "Error generating system prompt" });
        }
    }

    /// <summary>
    /// Gets the version history of Soul configurations.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<SoulVersionDto>>> GetHistory(CancellationToken ct)
    {
        try
        {
            var history = await _soulService.GetHistoryAsync(ct);

            return Ok(history.Select(v => new SoulVersionDto
            {
                Id = v.Id,
                ChangedBy = v.ChangedBy,
                CreatedAt = v.CreatedAt,
                IsActive = v.IsActive
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting soul history");
            return StatusCode(500, new { error = "Error retrieving soul history" });
        }
    }

    /// <summary>
    /// Reverts to a previous version.
    /// </summary>
    [HttpPost("revert/{versionId}")]
    public async Task<ActionResult> RevertToVersion(Guid versionId, CancellationToken ct)
    {
        try
        {
            await _soulService.RevertToVersionAsync(versionId, ct);

            return Ok(new { message = "Soul configuration reverted successfully" });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting soul configuration");
            return StatusCode(500, new { error = "Error reverting soul configuration" });
        }
    }
}

#region DTOs

public class SoulConfigurationDto
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> PrimaryDirectives { get; set; } = new();
    public ToneConfigurationDto Tone { get; set; } = new();
    public BoundaryConfigurationDto Boundaries { get; set; } = new();
    public WorkingHoursConfigurationDto WorkingHours { get; set; } = new();
    public Dictionary<string, string> ResponseTemplates { get; set; } = new();
}

public class ToneConfigurationDto
{
    public string CommunicationStyle { get; set; } = string.Empty;
    public string HumorLevel { get; set; } = string.Empty;
    public string Verbosity { get; set; } = string.Empty;
    public string FormalityWithOthers { get; set; } = string.Empty;
}

public class BoundaryConfigurationDto
{
    public List<string> TopicsToAvoid { get; set; } = new();
    public List<string> ActionsNeverToTakeAutonomously { get; set; } = new();
    public List<string> InformationNeverToShare { get; set; } = new();
}

public class WorkingHoursConfigurationDto
{
    public string ActiveHours { get; set; } = string.Empty;
    public string UrgentOverride { get; set; } = string.Empty;
}

public class SoulMarkdownDto
{
    public string Content { get; set; } = string.Empty;
}

public class SystemPromptDto
{
    public string Prompt { get; set; } = string.Empty;
}

public class SoulVersionDto
{
    public Guid Id { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}

#endregion