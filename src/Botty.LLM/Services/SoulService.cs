using System.Text;
using System.Text.RegularExpressions;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.LLM.Services;

/// <summary>
/// Configuration options for Soul service.
/// </summary>
public class SoulOptions
{
    /// <summary>
    /// Path to the Soul.md file.
    /// </summary>
    public string FilePath { get; set; } = "config/Soul.md";
}

/// <summary>
/// Implementation of ISoulService that loads Soul.md from disk.
/// </summary>
public partial class SoulService : ISoulService
{
    private readonly SoulOptions _options;
    private readonly ILogger<SoulService> _logger;
    private SoulConfiguration? _cachedConfiguration;
    private readonly List<SoulVersion> _versionHistory = new();
    private DateTime _lastLoadTime = DateTime.MinValue;

    public SoulService(
        IOptions<SoulOptions> options,
        ILogger<SoulService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SoulConfiguration> GetCurrentAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _cachedConfiguration!;
    }

    public async Task<SoulConfiguration> UpdateAsync(string markdown, string changedBy, CancellationToken ct = default)
    {
        var filePath = GetFilePath();
        var directory = Path.GetDirectoryName(filePath);
        
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Archive the current version
        if (_cachedConfiguration != null)
        {
            foreach (var version in _versionHistory.Where(v => v.IsActive))
            {
                version.IsActive = false;
            }
        }

        // Save new version
        await File.WriteAllTextAsync(filePath, markdown, ct);
        
        var newVersion = new SoulVersion
        {
            Id = Guid.NewGuid(),
            Content = markdown,
            ChangedBy = changedBy,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        _versionHistory.Add(newVersion);

        // Parse and cache the new configuration
        _cachedConfiguration = ParseMarkdown(markdown);
        
        _logger.LogInformation("Soul.md updated by {ChangedBy}", changedBy);

        return _cachedConfiguration;
    }

    public Task<IEnumerable<SoulVersion>> GetHistoryAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IEnumerable<SoulVersion>>(
            _versionHistory.OrderByDescending(v => v.CreatedAt).ToList());
    }

    public async Task<SoulConfiguration> RevertToVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var version = _versionHistory.FirstOrDefault(v => v.Id == versionId);
        if (version == null)
        {
            throw new ArgumentException($"Version {versionId} not found", nameof(versionId));
        }

        return await UpdateAsync(version.Content, "revert", ct);
    }

    public string GenerateSystemPrompt(SoulConfiguration soul, string memoryPack)
    {
        var sb = new StringBuilder();

        // Identity
        sb.AppendLine($"You are {soul.Name ?? "Botty"}, {soul.Role ?? "Personal AI assistant"}.");
        sb.AppendLine();

        // Primary Directives
        if (soul.Directives.Count > 0)
        {
            sb.AppendLine("## Primary Directives");
            foreach (var directive in soul.Directives)
            {
                sb.AppendLine($"- {directive}");
            }
            sb.AppendLine();
        }

        // Tone & Personality
        sb.AppendLine("## Tone & Personality");
        sb.AppendLine($"- Communication style: {soul.Tone.CommunicationStyle}");
        sb.AppendLine($"- Humor level: {soul.Tone.HumorLevel}");
        sb.AppendLine($"- Verbosity: {soul.Tone.Verbosity}");
        sb.AppendLine($"- When communicating with others on behalf of the user: {soul.Tone.FormalityWithOthers}");
        sb.AppendLine();

        // Boundaries
        if (soul.Boundaries.NeverAutonomousActions.Count > 0 ||
            soul.Boundaries.NeverShareInfo.Count > 0 ||
            soul.Boundaries.TopicsToAvoid.Count > 0)
        {
            sb.AppendLine("## Boundaries");
            
            if (soul.Boundaries.NeverAutonomousActions.Count > 0)
            {
                sb.AppendLine("### Actions NEVER to take without explicit user approval:");
                foreach (var action in soul.Boundaries.NeverAutonomousActions)
                {
                    sb.AppendLine($"- {action}");
                }
            }

            if (soul.Boundaries.NeverShareInfo.Count > 0)
            {
                sb.AppendLine("### Information NEVER to share:");
                foreach (var info in soul.Boundaries.NeverShareInfo)
                {
                    sb.AppendLine($"- {info}");
                }
            }

            if (soul.Boundaries.TopicsToAvoid.Count > 0)
            {
                sb.AppendLine("### Topics to avoid:");
                foreach (var topic in soul.Boundaries.TopicsToAvoid)
                {
                    sb.AppendLine($"- {topic}");
                }
            }
            sb.AppendLine();
        }

        // Working Hours
        if (soul.WorkingHours.ActiveHours != null)
        {
            sb.AppendLine("## Working Hours");
            sb.AppendLine($"Active hours: {soul.WorkingHours.ActiveHours}");
            if (!string.IsNullOrWhiteSpace(soul.WorkingHours.UrgentOverride))
            {
                sb.AppendLine($"Override for: {soul.WorkingHours.UrgentOverride}");
            }
            sb.AppendLine();
        }

        // Response Guidelines
        if (soul.ResponseTemplates.Count > 0)
        {
            sb.AppendLine("## Response Guidelines");
            foreach (var template in soul.ResponseTemplates)
            {
                sb.AppendLine($"- When {template.Key.ToLowerInvariant()}: {template.Value}");
            }
            sb.AppendLine();
        }

        // Memory Pack
        if (!string.IsNullOrWhiteSpace(memoryPack))
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(memoryPack);
        }

        return sb.ToString();
    }

    public SoulConfiguration ParseMarkdown(string markdown)
    {
        var config = new SoulConfiguration
        {
            RawContent = markdown
        };

        // Parse Identity
        var nameMatch = NameRegex().Match(markdown);
        if (nameMatch.Success)
        {
            config.Name = nameMatch.Groups[1].Value.Trim();
        }

        var roleMatch = RoleRegex().Match(markdown);
        if (roleMatch.Success)
        {
            config.Role = roleMatch.Groups[1].Value.Trim();
        }

        // Parse Primary Directives
        var directivesSection = ExtractSection(markdown, "Primary Directives");
        if (!string.IsNullOrEmpty(directivesSection))
        {
            config.Directives = ExtractListItems(directivesSection);
        }

        // Parse Tone & Personality
        var toneSection = ExtractSection(markdown, "Tone & Personality");
        if (!string.IsNullOrEmpty(toneSection))
        {
            config.Tone = ParseTone(toneSection);
        }

        // Parse Boundaries
        var boundariesSection = ExtractSection(markdown, "Boundaries");
        if (!string.IsNullOrEmpty(boundariesSection))
        {
            config.Boundaries = ParseBoundaries(boundariesSection);
        }

        // Parse Working Hours
        var hoursSection = ExtractSection(markdown, "Working Hours");
        if (!string.IsNullOrEmpty(hoursSection))
        {
            config.WorkingHours = ParseWorkingHours(hoursSection);
        }

        // Parse Response Templates
        var templatesSection = ExtractSection(markdown, "Response Templates");
        if (!string.IsNullOrEmpty(templatesSection))
        {
            config.ResponseTemplates = ParseResponseTemplates(templatesSection);
        }

        return config;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_cachedConfiguration != null)
        {
            return;
        }

        var filePath = GetFilePath();

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Soul.md not found at {Path}, using defaults", filePath);
            _cachedConfiguration = new SoulConfiguration { RawContent = string.Empty };
            return;
        }

        var content = await File.ReadAllTextAsync(filePath, ct);
        _cachedConfiguration = ParseMarkdown(content);
        
        // Add to version history
        _versionHistory.Add(new SoulVersion
        {
            Id = Guid.NewGuid(),
            Content = content,
            ChangedBy = "system",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        
        _lastLoadTime = DateTime.UtcNow;

        _logger.LogInformation("Loaded Soul.md from {Path}", filePath);
    }

    private string GetFilePath()
    {
        var filePath = _options.FilePath;
        
        // If relative path, resolve from current directory
        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.Combine(Directory.GetCurrentDirectory(), filePath);
        }

        return filePath;
    }

    private static string ExtractSection(string content, string sectionName)
    {
        var headerPattern = $@"##\s*{Regex.Escape(sectionName)}\s*\r?\n";
        var headerMatch = Regex.Match(content, headerPattern, RegexOptions.IgnoreCase);
        
        if (!headerMatch.Success)
        {
            return string.Empty;
        }

        var startIndex = headerMatch.Index + headerMatch.Length;
        var nextSectionMatch = Regex.Match(content.Substring(startIndex), @"^##\s+[^#]", RegexOptions.Multiline);
        
        var endIndex = nextSectionMatch.Success 
            ? startIndex + nextSectionMatch.Index 
            : content.Length;

        return content.Substring(startIndex, endIndex - startIndex).Trim();
    }

    private static List<string> ExtractListItems(string section)
    {
        var items = new List<string>();
        var matches = Regex.Matches(section, @"^[-\d.]+\s*(.+)$", RegexOptions.Multiline);
        
        foreach (Match match in matches)
        {
            var item = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(item) && item != "None specified")
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static ToneSettings ParseTone(string section)
    {
        var tone = new ToneSettings();

        var styleMatch = Regex.Match(section, @"\*\*Communication style\*\*:\s*(.+)$", RegexOptions.Multiline);
        if (styleMatch.Success)
        {
            tone.CommunicationStyle = styleMatch.Groups[1].Value.Trim();
        }

        var humorMatch = Regex.Match(section, @"\*\*Humor level\*\*:\s*(.+)$", RegexOptions.Multiline);
        if (humorMatch.Success)
        {
            tone.HumorLevel = humorMatch.Groups[1].Value.Trim();
        }

        var verbosityMatch = Regex.Match(section, @"\*\*Verbosity\*\*:\s*(.+)$", RegexOptions.Multiline);
        if (verbosityMatch.Success)
        {
            tone.Verbosity = verbosityMatch.Groups[1].Value.Trim();
        }

        var formalityMatch = Regex.Match(section, @"\*\*Formality with others\*\*:\s*(.+)$", RegexOptions.Multiline);
        if (formalityMatch.Success)
        {
            tone.FormalityWithOthers = formalityMatch.Groups[1].Value.Trim();
        }

        return tone;
    }

    private static BoundarySettings ParseBoundaries(string section)
    {
        var boundaries = new BoundarySettings();

        var topicsSection = ExtractSubSection(section, "Topics to Avoid");
        if (!string.IsNullOrEmpty(topicsSection))
        {
            boundaries.TopicsToAvoid = ExtractListItems(topicsSection);
        }

        var actionsSection = ExtractSubSection(section, "Actions Never to Take Autonomously");
        if (!string.IsNullOrEmpty(actionsSection))
        {
            boundaries.NeverAutonomousActions = ExtractListItems(actionsSection);
        }

        var infoSection = ExtractSubSection(section, "Information Never to Share");
        if (!string.IsNullOrEmpty(infoSection))
        {
            boundaries.NeverShareInfo = ExtractListItems(infoSection);
        }

        return boundaries;
    }

    private static string ExtractSubSection(string content, string sectionName)
    {
        var headerPattern = $@"###\s*{Regex.Escape(sectionName)}\s*\r?\n";
        var headerMatch = Regex.Match(content, headerPattern, RegexOptions.IgnoreCase);
        
        if (!headerMatch.Success)
        {
            return string.Empty;
        }

        var startIndex = headerMatch.Index + headerMatch.Length;
        var nextSectionMatch = Regex.Match(content.Substring(startIndex), @"^###?\s+[^#]", RegexOptions.Multiline);
        
        var endIndex = nextSectionMatch.Success 
            ? startIndex + nextSectionMatch.Index 
            : content.Length;

        return content.Substring(startIndex, endIndex - startIndex).Trim();
    }

    private static WorkingHoursSettings ParseWorkingHours(string section)
    {
        var hours = new WorkingHoursSettings();

        var activeMatch = Regex.Match(section, @"\*\*Active hours\*\*:\s*(.+)$", RegexOptions.Multiline);
        if (activeMatch.Success)
        {
            hours.ActiveHours = activeMatch.Groups[1].Value.Trim();
        }

        var urgentMatch = Regex.Match(section, @"\*\*Urgent override\*\*:\s*(.+)$", RegexOptions.Multiline);
        if (urgentMatch.Success)
        {
            hours.UrgentOverride = urgentMatch.Groups[1].Value.Trim();
        }

        return hours;
    }

    private static Dictionary<string, string> ParseResponseTemplates(string section)
    {
        var templates = new Dictionary<string, string>();

        var matches = Regex.Matches(section, @"###\s+(.+?)\s*\r?\n([\s\S]*?)(?=###|$)");
        
        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value.Trim();
            var template = match.Groups[2].Value.Trim();
            
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(template))
            {
                templates[name] = template;
            }
        }

        return templates;
    }

    [GeneratedRegex(@"\*\*Name\*\*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"\*\*Role\*\*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex RoleRegex();
}
