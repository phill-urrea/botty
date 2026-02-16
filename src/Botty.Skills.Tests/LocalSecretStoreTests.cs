using Botty.Secrets.Implementations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Botty.Skills.Tests;

public class LocalSecretStoreTests
{
    [Fact]
    public async Task SetSecret_PersistsAcrossStoreInstances()
    {
        var secretsDir = CreateTempDirectory();
        try
        {
            var config = BuildConfiguration(secretsDir);
            var store1 = new LocalSecretStore(config, NullLogger<LocalSecretStore>.Instance);
            await store1.SetSecretAsync("oauth/accounts/test/refresh_token", "refresh-123");

            var store2 = new LocalSecretStore(config, NullLogger<LocalSecretStore>.Instance);
            var loaded = await store2.GetSecretAsync("oauth/accounts/test/refresh_token");

            Assert.Equal("refresh-123", loaded);
        }
        finally
        {
            SafeDeleteDirectory(secretsDir);
        }
    }

    [Fact]
    public async Task DeleteSecret_RemovesPersistedValue()
    {
        var secretsDir = CreateTempDirectory();
        try
        {
            var config = BuildConfiguration(secretsDir);
            var store = new LocalSecretStore(config, NullLogger<LocalSecretStore>.Instance);
            await store.SetSecretAsync("skills/gmail/client_secret", "secret-abc");

            await store.DeleteSecretAsync("skills/gmail/client_secret");
            var value = await store.GetSecretAsync("skills/gmail/client_secret");

            Assert.Null(value);
        }
        finally
        {
            SafeDeleteDirectory(secretsDir);
        }
    }

    private static IConfiguration BuildConfiguration(string secretsDir)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:LocalDirectory"] = secretsDir
            })
            .Build();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"botty-secrets-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temp test data.
        }
    }
}
