using Botty.Core.Interfaces;

namespace Botty.Channels;

/// <summary>
/// Configuration for a channel plugin
/// </summary>
public class ChannelConfig
{
    private readonly ISecretStore _secretStore;
    private readonly string _channelId;
    private readonly Dictionary<string, string> _values;
    
    public ChannelConfig(
        string channelId,
        Dictionary<string, string> values,
        ISecretStore secretStore)
    {
        _channelId = channelId;
        _values = values;
        _secretStore = secretStore;
    }
    
    /// <summary>
    /// Get a configuration value
    /// </summary>
    public string? GetValue(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }
    
    /// <summary>
    /// Get a configuration value with default
    /// </summary>
    public string GetValue(string key, string defaultValue)
    {
        return _values.TryGetValue(key, out var value) ? value : defaultValue;
    }
    
    /// <summary>
    /// Get a secret from the secret store
    /// </summary>
    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        var secretKey = $"channel_{_channelId}_{key}";
        return await _secretStore.GetSecretAsync(secretKey, ct);
    }
    
    /// <summary>
    /// Get a required secret from the secret store
    /// </summary>
    public async Task<string> GetRequiredSecretAsync(string key, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, ct);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"Required secret 'channel_{_channelId}_{key}' is not configured");
        }
        return value;
    }
    
    /// <summary>
    /// Check if the channel is enabled in configuration
    /// </summary>
    public bool IsEnabled => GetValue("enabled", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Schema for channel configuration
/// </summary>
public class ChannelConfigSchema
{
    public required string ChannelId { get; init; }
    public List<ChannelConfigField> Fields { get; init; } = [];
}

/// <summary>
/// A configuration field for a channel
/// </summary>
public class ChannelConfigField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public ChannelConfigFieldType Type { get; init; } = ChannelConfigFieldType.String;
    public bool IsSensitive { get; init; } = false;
    public bool IsRequired { get; init; } = false;
    public string? DefaultValue { get; init; }
}

public enum ChannelConfigFieldType
{
    String,
    Number,
    Boolean,
    Secret
}
