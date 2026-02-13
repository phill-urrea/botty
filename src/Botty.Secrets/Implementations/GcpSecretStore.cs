using Botty.Core.Interfaces;
using Google.Cloud.SecretManager.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.Secrets.Implementations;

/// <summary>
/// GCP Secret Manager implementation for production.
/// </summary>
public class GcpSecretStore : ISecretStore
{
    private readonly SecretManagerServiceClient _client;
    private readonly GcpSecretStoreOptions _options;
    private readonly ILogger<GcpSecretStore> _logger;

    public GcpSecretStore(
        SecretManagerServiceClient client,
        IOptions<GcpSecretStoreOptions> options,
        ILogger<GcpSecretStore> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var secretPath = GetSecretPath(key);
            var versionPath = $"{secretPath}/versions/latest";
            
            var response = await _client.AccessSecretVersionAsync(versionPath, ct);
            var secretValue = response.Payload.Data.ToStringUtf8();
            
            _logger.LogDebug("Retrieved secret {Key} from GCP Secret Manager", key);
            return secretValue;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            _logger.LogDebug("Secret {Key} not found in GCP Secret Manager", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret {Key} from GCP Secret Manager", key);
            throw;
        }
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    {
        try
        {
            var secretPath = GetSecretPath(key);
            var parentPath = $"projects/{_options.ProjectId}";

            // Check if secret exists
            try
            {
                await _client.GetSecretAsync(secretPath, ct);
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                // Create the secret first
                var secret = new Secret
                {
                    Replication = new Replication
                    {
                        Automatic = new Replication.Types.Automatic()
                    }
                };
                
                var secretId = GetSecretId(key);
                await _client.CreateSecretAsync(parentPath, secretId, secret, ct);
                _logger.LogInformation("Created new secret {Key} in GCP Secret Manager", key);
            }

            // Add the secret version
            var payload = new SecretPayload
            {
                Data = Google.Protobuf.ByteString.CopyFromUtf8(value)
            };

            await _client.AddSecretVersionAsync(secretPath, payload, ct);
            _logger.LogInformation("Set secret {Key} in GCP Secret Manager", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting secret {Key} in GCP Secret Manager", key);
            throw;
        }
    }

    public async Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var secretPath = GetSecretPath(key);
            await _client.DeleteSecretAsync(secretPath, ct);
            _logger.LogInformation("Deleted secret {Key} from GCP Secret Manager", key);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            _logger.LogDebug("Secret {Key} not found when attempting to delete", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secret {Key} from GCP Secret Manager", key);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var secretPath = GetSecretPath(key);
            await _client.GetSecretAsync(secretPath, ct);
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return false;
        }
    }

    private string GetSecretPath(string key)
    {
        var secretId = GetSecretId(key);
        return $"projects/{_options.ProjectId}/secrets/{secretId}";
    }

    private string GetSecretId(string key)
    {
        // Convert key to a valid secret ID (alphanumeric, underscores, hyphens)
        var secretId = $"botty_{key.Replace(".", "_").Replace("/", "_").Replace(":", "_")}";
        return secretId.ToLowerInvariant();
    }
}

/// <summary>
/// Options for GCP Secret Store.
/// </summary>
public class GcpSecretStoreOptions
{
    /// <summary>
    /// GCP Project ID.
    /// </summary>
    public required string ProjectId { get; set; }
}
