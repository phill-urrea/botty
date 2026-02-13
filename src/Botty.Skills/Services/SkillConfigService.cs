using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Botty.Skills.Services;

/// <summary>
/// Service for managing skill configurations with sensitive data routing to secret store.
/// </summary>
public class SkillConfigService : ISkillConfigService
{
    private readonly ISkillConfigRepository _repository;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<SkillConfigService> _logger;
    private readonly ConcurrentDictionary<string, SkillConfigSchema> _schemas = new();

    public SkillConfigService(
        ISkillConfigRepository repository,
        ISecretStore secretStore,
        ILogger<SkillConfigService> logger)
    {
        _repository = repository;
        _secretStore = secretStore;
        _logger = logger;
    }

    /// <summary>
    /// Registers a skill's configuration schema.
    /// </summary>
    public void RegisterSchema(SkillConfigSchema schema)
    {
        _schemas[schema.SkillId] = schema;
        _logger.LogDebug("Registered config schema for skill {SkillId}", schema.SkillId);
    }

    /// <inheritdoc />
    public Task<SkillConfigSchema> GetSchemaAsync(string skillId, CancellationToken ct = default)
    {
        if (_schemas.TryGetValue(skillId, out var schema))
        {
            return Task.FromResult(schema);
        }
        throw new InvalidOperationException($"No schema registered for skill: {skillId}");
    }

    /// <inheritdoc />
    public async Task<SkillConfiguration> GetConfigAsync(string skillId, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(skillId, ct);
        var config = new SkillConfiguration { SkillId = skillId };

        // Get non-sensitive values from repository
        var dbValues = await _repository.GetAllValuesAsync(skillId, ct);
        
        foreach (var field in schema.Fields)
        {
            if (field.IsSensitive)
            {
                // Get sensitive values from secret store
                var secretPath = GetSecretPath(skillId, field.Key);
                var secretValue = await _secretStore.GetSecretAsync(secretPath, ct);
                config.Values[field.Key] = secretValue;
            }
            else
            {
                // Get from database
                var dbValue = dbValues.FirstOrDefault(v => v.Key == field.Key);
                config.Values[field.Key] = dbValue?.Value ?? field.DefaultValue;
            }
        }

        return config;
    }

    /// <inheritdoc />
    public async Task SetConfigValueAsync(string skillId, string key, string value, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(skillId, ct);
        var field = schema.Fields.FirstOrDefault(f => f.Key == key);
        
        if (field == null)
        {
            throw new ArgumentException($"Unknown configuration field: {key}");
        }

        if (field.IsSensitive)
        {
            // Store in secret store
            var secretPath = GetSecretPath(skillId, key);
            await _secretStore.SetSecretAsync(secretPath, value, ct);
            
            // Store reference in database (without value)
            await _repository.SetValueAsync(new SkillConfigValue
            {
                Id = Guid.NewGuid(),
                SkillId = skillId,
                Key = key,
                Value = null, // Don't store sensitive value in DB
                IsSensitive = true,
                UpdatedAt = DateTime.UtcNow
            }, ct);
            
            _logger.LogInformation("Set sensitive config {Key} for skill {SkillId} (stored in secret store)", 
                key, skillId);
        }
        else
        {
            // Store in database
            await _repository.SetValueAsync(new SkillConfigValue
            {
                Id = Guid.NewGuid(),
                SkillId = skillId,
                Key = key,
                Value = value,
                IsSensitive = false,
                UpdatedAt = DateTime.UtcNow
            }, ct);
            
            _logger.LogDebug("Set config {Key} for skill {SkillId}", key, skillId);
        }
    }

    /// <inheritdoc />
    public async Task UpdateConfigAsync(string skillId, Dictionary<string, string> values, CancellationToken ct = default)
    {
        foreach (var (key, value) in values)
        {
            await SetConfigValueAsync(skillId, key, value, ct);
        }
    }

    /// <inheritdoc />
    public async Task<ConfigValidationResult> ValidateConfigAsync(string skillId, CancellationToken ct = default)
    {
        var result = new ConfigValidationResult { IsValid = true };
        var schema = await GetSchemaAsync(skillId, ct);
        var config = await GetConfigAsync(skillId, ct);

        foreach (var field in schema.Fields)
        {
            var value = config.GetValue(field.Key);

            // Check required fields
            if (field.IsRequired && string.IsNullOrEmpty(value))
            {
                result.IsValid = false;
                result.Errors.Add(new ConfigValidationError
                {
                    Field = field.Key,
                    Message = $"Required field '{field.Label}' is not set"
                });
                continue;
            }

            // Check regex validation
            if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(field.ValidationRegex))
            {
                if (!Regex.IsMatch(value, field.ValidationRegex))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ConfigValidationError
                    {
                        Field = field.Key,
                        Message = $"Field '{field.Label}' does not match required format"
                    });
                }
            }

            // Check selection options
            if (!string.IsNullOrEmpty(value) && field.Options != null && field.Options.Count > 0)
            {
                if (!field.Options.Any(o => o.Value == value))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ConfigValidationError
                    {
                        Field = field.Key,
                        Message = $"Field '{field.Label}' has invalid value"
                    });
                }
            }

            // Check basic type constraints
            if (!string.IsNullOrWhiteSpace(value))
            {
                switch (field.Type)
                {
                    case Botty.Core.Enums.ConfigFieldType.Number:
                        if (!double.TryParse(value, out _))
                        {
                            result.IsValid = false;
                            result.Errors.Add(new ConfigValidationError
                            {
                                Field = field.Key,
                                Message = $"Field '{field.Label}' must be a valid number"
                            });
                        }
                        break;
                    case Botty.Core.Enums.ConfigFieldType.Boolean:
                        if (!bool.TryParse(value, out _))
                        {
                            result.IsValid = false;
                            result.Errors.Add(new ConfigValidationError
                            {
                                Field = field.Key,
                                Message = $"Field '{field.Label}' must be true or false"
                            });
                        }
                        break;
                    case Botty.Core.Enums.ConfigFieldType.Json:
                        try
                        {
                            JsonDocument.Parse(value);
                        }
                        catch (JsonException)
                        {
                            result.IsValid = false;
                            result.Errors.Add(new ConfigValidationError
                            {
                                Field = field.Key,
                                Message = $"Field '{field.Label}' must be valid JSON"
                            });
                        }
                        break;
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task DeleteConfigValueAsync(string skillId, string key, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(skillId, ct);
        var field = schema.Fields.FirstOrDefault(f => f.Key == key);

        if (field?.IsSensitive == true)
        {
            var secretPath = GetSecretPath(skillId, key);
            await _secretStore.DeleteSecretAsync(secretPath, ct);
        }

        await _repository.DeleteValueAsync(skillId, key, ct);
        _logger.LogInformation("Deleted config {Key} for skill {SkillId}", key, skillId);
    }

    /// <inheritdoc />
    public async Task<bool> HasValueAsync(string skillId, string key, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(skillId, ct);
        var field = schema.Fields.FirstOrDefault(f => f.Key == key);

        if (field?.IsSensitive == true)
        {
            var secretPath = GetSecretPath(skillId, key);
            var value = await _secretStore.GetSecretAsync(secretPath, ct);
            return !string.IsNullOrEmpty(value);
        }

        return await _repository.HasValueAsync(skillId, key, ct);
    }

    private static string GetSecretPath(string skillId, string key)
    {
        return $"skills/{skillId}/{key}";
    }
}

/// <summary>
/// Repository interface for skill configuration storage.
/// </summary>
public interface ISkillConfigRepository
{
    Task<List<SkillConfigValue>> GetAllValuesAsync(string skillId, CancellationToken ct = default);
    Task SetValueAsync(SkillConfigValue value, CancellationToken ct = default);
    Task DeleteValueAsync(string skillId, string key, CancellationToken ct = default);
    Task<bool> HasValueAsync(string skillId, string key, CancellationToken ct = default);
}
