using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Botty.Skills.Registry;

/// <summary>
/// Registry for managing skills.
/// </summary>
public class SkillRegistry : ISkillRegistry
{
    private readonly ConcurrentDictionary<string, ISkill> _skills = new();
    private readonly ISkillConfigService _configService;
    private readonly ILogger<SkillRegistry> _logger;

    public SkillRegistry(
        ISkillConfigService configService,
        ILogger<SkillRegistry> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Register(ISkill skill)
    {
        if (_skills.TryAdd(skill.Id, skill))
        {
            _logger.LogInformation("Registered skill: {SkillId} ({SkillName})", skill.Id, skill.Name);
        }
        else
        {
            _logger.LogWarning("Skill {SkillId} is already registered", skill.Id);
        }
    }

    /// <inheritdoc />
    public ISkill? Get(string id)
    {
        return _skills.TryGetValue(id, out var skill) ? skill : null;
    }

    /// <inheritdoc />
    public IEnumerable<ISkill> GetAll()
    {
        return _skills.Values;
    }

    /// <inheritdoc />
    public async Task<bool> IsConfiguredAsync(string skillId, CancellationToken ct = default)
    {
        var skill = Get(skillId);
        if (skill == null)
        {
            return false;
        }

        var validation = await _configService.ValidateConfigAsync(skillId, ct);
        return validation.IsValid;
    }

    /// <summary>
    /// Initializes all registered skills with their configurations.
    /// </summary>
    public async Task InitializeAllAsync(CancellationToken ct = default)
    {
        foreach (var skill in _skills.Values)
        {
            try
            {
                var config = await _configService.GetConfigAsync(skill.Id, ct);
                var validation = await _configService.ValidateConfigAsync(skill.Id, ct);
                
                if (validation.IsValid)
                {
                    await skill.InitializeAsync(config, ct);
                    _logger.LogInformation("Initialized skill: {SkillId}", skill.Id);
                }
                else
                {
                    _logger.LogWarning("Skill {SkillId} not fully configured: {Errors}", 
                        skill.Id, 
                        string.Join(", ", validation.Errors.Select(e => e.Message)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize skill: {SkillId}", skill.Id);
            }
        }
    }

    /// <summary>
    /// Gets all tools from all initialized skills.
    /// </summary>
    public IEnumerable<LlmTool> GetAllTools()
    {
        return _skills.Values.SelectMany(s => s.GetTools());
    }

    /// <summary>
    /// Executes a tool call by finding the appropriate skill.
    /// </summary>
    public async Task<SkillResult> ExecuteToolAsync(string toolName, string arguments, CancellationToken ct = default)
    {
        foreach (var skill in _skills.Values)
        {
            var tools = skill.GetTools();
            if (tools.Any(t => t.Name == toolName))
            {
                var context = new SkillContext
                {
                    ToolName = toolName,
                    Arguments = arguments
                };
                return await skill.ExecuteAsync(context, ct);
            }
        }

        return SkillResult.Fail($"No skill found for tool: {toolName}");
    }
}
