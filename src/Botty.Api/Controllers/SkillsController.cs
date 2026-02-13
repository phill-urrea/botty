using Botty.Core.Interfaces;
using Botty.Skills.Registry;
using Botty.Skills.Services;
using Microsoft.AspNetCore.Mvc;

namespace Botty.Api.Controllers;

/// <summary>
/// Controller for managing skills and their configurations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SkillsController : ControllerBase
{
    private readonly ISkillRegistry _registry;
    private readonly ISkillConfigService _configService;
    private readonly SkillRegistry _skillRegistry;
    private readonly ILogger<SkillsController> _logger;

    public SkillsController(
        ISkillRegistry registry,
        ISkillConfigService configService,
        SkillRegistry skillRegistry,
        ILogger<SkillsController> logger)
    {
        _registry = registry;
        _configService = configService;
        _skillRegistry = skillRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Lists all registered skills.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListSkills(CancellationToken ct)
    {
        var skills = _registry.GetAll().Select(s => new SkillDto
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description,
            ToolCount = s.GetTools().Count()
        }).ToList();

        // Check configuration status for each skill
        foreach (var skill in skills)
        {
            skill.IsConfigured = await _registry.IsConfiguredAsync(skill.Id, ct);
        }

        return Ok(new { skills, count = skills.Count });
    }

    /// <summary>
    /// Gets details of a specific skill.
    /// </summary>
    [HttpGet("{skillId}")]
    public async Task<IActionResult> GetSkill(string skillId, CancellationToken ct)
    {
        var skill = _registry.Get(skillId);
        if (skill == null)
        {
            return NotFound(new { error = $"Skill '{skillId}' not found" });
        }

        var isConfigured = await _registry.IsConfiguredAsync(skillId, ct);
        var validation = await _configService.ValidateConfigAsync(skillId, ct);

        return Ok(new
        {
            id = skill.Id,
            name = skill.Name,
            description = skill.Description,
            isConfigured,
            validationErrors = validation.Errors.Select(e => new { field = e.Field, message = e.Message }),
            tools = skill.GetTools().Select(t => new
            {
                name = t.Name,
                description = t.Description,
                parametersSchema = t.ParametersSchema
            })
        });
    }

    /// <summary>
    /// Gets the configuration schema for a skill.
    /// </summary>
    [HttpGet("{skillId}/config/schema")]
    public async Task<IActionResult> GetConfigSchema(string skillId, CancellationToken ct)
    {
        try
        {
            var schema = await _configService.GetSchemaAsync(skillId, ct);
            return Ok(new
            {
                skillId = schema.SkillId,
                fields = schema.Fields.Select(f => new
                {
                    key = f.Key,
                    label = f.Label,
                    description = f.Description,
                    type = f.Type.ToString().ToLowerInvariant(),
                    isSensitive = f.IsSensitive,
                    isRequired = f.IsRequired,
                    defaultValue = f.DefaultValue,
                    options = f.Options?.Select(o => new { value = o.Value, label = o.Label })
                })
            });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = $"Skill '{skillId}' not found" });
        }
    }

    /// <summary>
    /// Gets the current configuration for a skill (without sensitive values).
    /// </summary>
    [HttpGet("{skillId}/config")]
    public async Task<IActionResult> GetConfig(string skillId, CancellationToken ct)
    {
        try
        {
            var schema = await _configService.GetSchemaAsync(skillId, ct);
            var config = await _configService.GetConfigAsync(skillId, ct);

            // Don't return actual sensitive values, just whether they are set
            var values = new Dictionary<string, object?>();
            foreach (var field in schema.Fields)
            {
                if (field.IsSensitive)
                {
                    var hasValue = await _configService.HasValueAsync(skillId, field.Key, ct);
                    values[field.Key] = hasValue ? "********" : null;
                }
                else
                {
                    values[field.Key] = config.GetValue(field.Key);
                }
            }

            return Ok(new { skillId, values });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = $"Skill '{skillId}' not found" });
        }
    }

    /// <summary>
    /// Updates configuration for a skill.
    /// </summary>
    [HttpPut("{skillId}/config")]
    public async Task<IActionResult> UpdateConfig(
        string skillId,
        [FromBody] UpdateConfigRequest request,
        CancellationToken ct)
    {
        try
        {
            await _configService.UpdateConfigAsync(skillId, request.Values, ct);

            // Re-initialize the skill with new config
            var skill = _registry.Get(skillId);
            if (skill != null)
            {
                var config = await _configService.GetConfigAsync(skillId, ct);
                await skill.InitializeAsync(config, ct);
            }

            return Ok(new { success = true, message = "Configuration updated" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = $"Skill '{skillId}' not found" });
        }
    }

    /// <summary>
    /// Sets a single configuration value.
    /// </summary>
    [HttpPut("{skillId}/config/{key}")]
    public async Task<IActionResult> SetConfigValue(
        string skillId,
        string key,
        [FromBody] SetConfigValueRequest request,
        CancellationToken ct)
    {
        try
        {
            await _configService.SetConfigValueAsync(skillId, key, request.Value, ct);

            // Re-initialize the skill so runtime state reflects latest config updates.
            var skill = _registry.Get(skillId);
            if (skill != null)
            {
                var config = await _configService.GetConfigAsync(skillId, ct);
                await skill.InitializeAsync(config, ct);
            }

            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Deletes a configuration value.
    /// </summary>
    [HttpDelete("{skillId}/config/{key}")]
    public async Task<IActionResult> DeleteConfigValue(string skillId, string key, CancellationToken ct)
    {
        try
        {
            await _configService.DeleteConfigValueAsync(skillId, key, ct);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting config {Key} for skill {SkillId}", key, skillId);
            return StatusCode(500, new { error = "Failed to delete configuration" });
        }
    }

    /// <summary>
    /// Validates a skill's configuration.
    /// </summary>
    [HttpPost("{skillId}/config/validate")]
    public async Task<IActionResult> ValidateConfig(string skillId, CancellationToken ct)
    {
        try
        {
            var result = await _configService.ValidateConfigAsync(skillId, ct);
            return Ok(new
            {
                isValid = result.IsValid,
                errors = result.Errors.Select(e => new { field = e.Field, message = e.Message })
            });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = $"Skill '{skillId}' not found" });
        }
    }

    /// <summary>
    /// Executes a skill tool directly (mainly for testing).
    /// </summary>
    [HttpPost("{skillId}/execute")]
    public async Task<IActionResult> ExecuteTool(
        string skillId,
        [FromBody] ExecuteToolRequest request,
        CancellationToken ct)
    {
        var skill = _registry.Get(skillId);
        if (skill == null)
        {
            return NotFound(new { error = $"Skill '{skillId}' not found" });
        }

        var tools = skill.GetTools();
        if (!tools.Any(t => t.Name == request.ToolName))
        {
            return BadRequest(new { error = $"Tool '{request.ToolName}' not found in skill '{skillId}'" });
        }

        var context = new SkillContext
        {
            ToolName = request.ToolName,
            Arguments = request.Arguments ?? "{}"
        };

        var result = await skill.ExecuteAsync(context, ct);

        return Ok(new
        {
            success = result.Success,
            result = result.Result,
            error = result.Error
        });
    }

    /// <summary>
    /// Gets all tools from all skills.
    /// </summary>
    [HttpGet("tools")]
    public IActionResult ListAllTools()
    {
        var tools = _skillRegistry.GetAllTools().Select(t => new
        {
            name = t.Name,
            description = t.Description,
            parametersSchema = t.ParametersSchema
        });

        return Ok(new { tools, count = tools.Count() });
    }
}

public class SkillDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int ToolCount { get; set; }
    public bool IsConfigured { get; set; }
}

public class UpdateConfigRequest
{
    public Dictionary<string, string> Values { get; set; } = new();
}

public class SetConfigValueRequest
{
    public required string Value { get; set; }
}

public class ExecuteToolRequest
{
    public required string ToolName { get; set; }
    public string? Arguments { get; set; }
}
