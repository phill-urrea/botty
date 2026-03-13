using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Botty.Tools.Services;

/// <summary>
/// Service for managing tool configurations with sensitive data routing to secret store.
/// </summary>
public class ToolConfigService : IToolConfigService
{
    private readonly IToolConfigRepository _repository;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<ToolConfigService> _logger;
    private readonly ConcurrentDictionary<string, ToolConfigSchema> _schemas = new();

    public ToolConfigService(
        IToolConfigRepository repository,
        ISecretStore secretStore,
        ILogger<ToolConfigService> logger)
    {
        _repository = repository;
        _secretStore = secretStore;
        _logger = logger;
    }

    /// <summary>
    /// Registers a tool's configuration schema.
    /// </summary>
    public void RegisterSchema(ToolConfigSchema schema)
    {
        _schemas[schema.ToolId] = schema;
        _logger.LogDebug("Registered config schema for tool {ToolId}", schema.ToolId);
    }

    /// <inheritdoc />
    public Task<ToolConfigSchema> GetSchemaAsync(string toolId, CancellationToken ct = default)
    {
        if (_schemas.TryGetValue(toolId, out var schema))
        {
            return Task.FromResult(schema);
        }
        throw new InvalidOperationException($"No schema registered for tool: {toolId}");
    }

    /// <inheritdoc />
    public async Task<ToolConfiguration> GetConfigAsync(string toolId, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(toolId, ct);
        var config = new ToolConfiguration { ToolId = toolId };

        // Get non-sensitive values from repository
        var dbValues = await _repository.GetAllValuesAsync(toolId, ct);
        
        foreach (var field in schema.Fields)
        {
            if (field.IsSensitive)
            {
                // Get sensitive values from secret store
                var secretPath = GetSecretPath(toolId, field.Key);
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
    public async Task SetConfigValueAsync(string toolId, string key, string value, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(toolId, ct);
        var field = schema.Fields.FirstOrDefault(f => f.Key == key);
        
        if (field == null)
        {
            throw new ArgumentException($"Unknown configuration field: {key}");
        }

        if (field.IsSensitive)
        {
            // Store in secret store
            var secretPath = GetSecretPath(toolId, key);
            await _secretStore.SetSecretAsync(secretPath, value, ct);
            
            // Store reference in database (without value)
            await _repository.SetValueAsync(new ToolConfigValue
            {
                Id = Guid.NewGuid(),
                ToolId = toolId,
                Key = key,
                Value = null, // Don't store sensitive value in DB
                IsSensitive = true,
                UpdatedAt = DateTime.UtcNow
            }, ct);
            
            _logger.LogInformation("Set sensitive config {Key} for tool {ToolId} (stored in secret store)", 
                key, toolId);
        }
        else
        {
            // Store in database
            await _repository.SetValueAsync(new ToolConfigValue
            {
                Id = Guid.NewGuid(),
                ToolId = toolId,
                Key = key,
                Value = value,
                IsSensitive = false,
                UpdatedAt = DateTime.UtcNow
            }, ct);
            
            _logger.LogDebug("Set config {Key} for tool {ToolId}", key, toolId);
        }
    }

    /// <inheritdoc />
    public async Task UpdateConfigAsync(string toolId, Dictionary<string, string> values, CancellationToken ct = default)
    {
        foreach (var (key, value) in values)
        {
            await SetConfigValueAsync(toolId, key, value, ct);
        }
    }

    /// <inheritdoc />
    public async Task<ConfigValidationResult> ValidateConfigAsync(string toolId, CancellationToken ct = default)
    {
        var result = new ConfigValidationResult { IsValid = true };
        var schema = await GetSchemaAsync(toolId, ct);
        var config = await GetConfigAsync(toolId, ct);

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
    public async Task DeleteConfigValueAsync(string toolId, string key, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(toolId, ct);
        var field = schema.Fields.FirstOrDefault(f => f.Key == key);

        if (field?.IsSensitive == true)
        {
            var secretPath = GetSecretPath(toolId, key);
            await _secretStore.DeleteSecretAsync(secretPath, ct);
        }

        await _repository.DeleteValueAsync(toolId, key, ct);
        _logger.LogInformation("Deleted config {Key} for tool {ToolId}", key, toolId);
    }

    /// <inheritdoc />
    public async Task<bool> HasValueAsync(string toolId, string key, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(toolId, ct);
        var field = schema.Fields.FirstOrDefault(f => f.Key == key);

        if (field?.IsSensitive == true)
        {
            var secretPath = GetSecretPath(toolId, key);
            var value = await _secretStore.GetSecretAsync(secretPath, ct);
            return !string.IsNullOrEmpty(value);
        }

        return await _repository.HasValueAsync(toolId, key, ct);
    }

    private static string GetSecretPath(string toolId, string key)
    {
        return $"tools/{toolId}/{key}";
    }
}

/// <summary>
/// Repository interface for tool configuration storage.
/// </summary>
public interface IToolConfigRepository
{
    Task<List<ToolConfigValue>> GetAllValuesAsync(string toolId, CancellationToken ct = default);
    Task SetValueAsync(ToolConfigValue value, CancellationToken ct = default);
    Task DeleteValueAsync(string toolId, string key, CancellationToken ct = default);
    Task<bool> HasValueAsync(string toolId, string key, CancellationToken ct = default);
}
