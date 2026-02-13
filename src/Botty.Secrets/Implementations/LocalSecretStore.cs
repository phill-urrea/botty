using Botty.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

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
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _secretsDirectory;

    public LocalSecretStore(IConfiguration configuration, ILogger<LocalSecretStore> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _secretsDirectory = ResolveSecretsDirectory(configuration);
        Directory.CreateDirectory(_secretsDirectory);

        _logger.LogInformation("Local secrets directory: {Path}", _secretsDirectory);
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        var normalizedKey = NormalizeKey(key);
        
        // First check in-memory cache
        if (_inMemorySecrets.TryGetValue(normalizedKey, out var cachedValue))
        {
            return Task.FromResult<string?>(cachedValue);
        }

        // Then check persisted local file secrets
        var persistedValue = TryReadPersistedSecret(normalizedKey);
        if (!string.IsNullOrEmpty(persistedValue))
        {
            _inMemorySecrets[normalizedKey] = persistedValue;
            return Task.FromResult<string?>(persistedValue);
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
        return PersistSecretAsync(normalizedKey, value, ct);
    }

    public async Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        var normalizedKey = NormalizeKey(key);
        _inMemorySecrets.Remove(normalizedKey);

        var filePath = GetSecretFilePath(normalizedKey);
        await _fileLock.WaitAsync(ct);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        finally
        {
            _fileLock.Release();
        }

        _logger.LogInformation("Deleted secret {Key} from local store", key);
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

    private async Task PersistSecretAsync(string normalizedKey, string value, CancellationToken ct)
    {
        var filePath = GetSecretFilePath(normalizedKey);
        var parentDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        await _fileLock.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(filePath, value, Encoding.UTF8, ct);
        }
        finally
        {
            _fileLock.Release();
        }

        _logger.LogInformation("Set secret {Key} in local store", normalizedKey);
    }

    private string? TryReadPersistedSecret(string normalizedKey)
    {
        var filePath = GetSecretFilePath(normalizedKey);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading persisted local secret {Key}", normalizedKey);
            return null;
        }
    }

    private string GetSecretFilePath(string normalizedKey)
    {
        var segments = normalizedKey.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizePathSegment)
            .ToArray();

        var relativePath = Path.Combine(segments) + ".secret";
        return Path.Combine(_secretsDirectory, relativePath);
    }

    private static string SanitizePathSegment(string segment)
    {
        var chars = segment.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' or '.'
                ? c
                : '_').ToArray();
        return new string(chars);
    }

    private static string ResolveSecretsDirectory(IConfiguration configuration)
    {
        var configured = configuration["Secrets:LocalDirectory"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (Directory.Exists("/app"))
        {
            return "/app/secrets";
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userHome))
        {
            return Path.Combine(userHome, ".botty", "secrets");
        }

        return Path.Combine(Environment.CurrentDirectory, ".botty-secrets");
    }
}
