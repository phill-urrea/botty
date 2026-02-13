using Botty.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Botty.Secrets.Implementations;

/// <summary>
/// Local secret store implementation for development.
/// Reads secrets from environment variables prefixed with BOTTY_SECRET_.
/// </summary>
public class LocalSecretStore : ISecretStore
{
    private const string SecretPrefix = "BOTTY_SECRET_";
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalSecretStore> _logger;
    private readonly Dictionary<string, string> _inMemorySecrets = new();

    public LocalSecretStore(IConfiguration configuration, ILogger<LocalSecretStore> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        var normalizedKey = NormalizeKey(key);
        
        // First check in-memory cache
        if (_inMemorySecrets.TryGetValue(normalizedKey, out var cachedValue))
        {
            return Task.FromResult<string?>(cachedValue);
        }

        // Then check environment variables
        var envKey = $"{SecretPrefix}{normalizedKey.ToUpperInvariant().Replace(".", "_").Replace("/", "_")}";
        var envValue = Environment.GetEnvironmentVariable(envKey);
        
        if (!string.IsNullOrEmpty(envValue))
        {
            _logger.LogDebug("Retrieved secret {Key} from environment variable", key);
            return Task.FromResult<string?>(envValue);
        }

        // Then check configuration
        var configKey = $"Secrets:{normalizedKey}";
        var configValue = _configuration[configKey];
        
        if (!string.IsNullOrEmpty(configValue))
        {
            _logger.LogDebug("Retrieved secret {Key} from configuration", key);
            return Task.FromResult<string?>(configValue);
        }

        _logger.LogDebug("Secret {Key} not found", key);
        return Task.FromResult<string?>(null);
    }

    public Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    {
        var normalizedKey = NormalizeKey(key);
        _inMemorySecrets[normalizedKey] = value;
        _logger.LogInformation("Set secret {Key} in local store (in-memory only)", key);
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        var normalizedKey = NormalizeKey(key);
        _inMemorySecrets.Remove(normalizedKey);
        _logger.LogInformation("Deleted secret {Key} from local store", key);
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, ct);
        return value is not null;
    }

    private static string NormalizeKey(string key)
    {
        return key.Trim().ToLowerInvariant();
    }
}
