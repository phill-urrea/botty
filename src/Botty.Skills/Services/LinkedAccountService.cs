using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Botty.Skills.Services;

/// <summary>
/// PostgreSQL-backed linked account store with token values in secret storage.
/// </summary>
public class LinkedAccountService : ILinkedAccountService
{
    private readonly string _connectionString;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<LinkedAccountService> _logger;

    public LinkedAccountService(
        string connectionString,
        ISecretStore secretStore,
        ILogger<LinkedAccountService> logger)
    {
        _connectionString = connectionString;
        _secretStore = secretStore;
        _logger = logger;
    }

    public async Task EnsureStoreAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            CREATE TABLE IF NOT EXISTS linked_accounts (
                id UUID PRIMARY KEY,
                provider VARCHAR(50) NOT NULL,
                email VARCHAR(255) NOT NULL,
                display_name VARCHAR(255),
                external_account_id VARCHAR(255),
                scope TEXT,
                refresh_token_secret_path VARCHAR(255) NOT NULL,
                access_token_secret_path VARCHAR(255),
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                last_linked_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE(provider, email)
            );

            CREATE INDEX IF NOT EXISTS idx_linked_accounts_provider ON linked_accounts(provider);
            CREATE INDEX IF NOT EXISTS idx_linked_accounts_email ON linked_accounts(email);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<LinkedAccount> UpsertAsync(
        OAuthProviderType provider,
        string email,
        string refreshToken,
        string? accessToken,
        string? displayName,
        string? externalAccountId,
        string? scope,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token is required.", nameof(refreshToken));

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await GetByProviderAndEmailAsync(provider, normalizedEmail, ct);
        var id = existing?.Id ?? Guid.NewGuid();

        var refreshPath = $"oauth/accounts/{id}/refresh_token";
        var accessPath = $"oauth/accounts/{id}/access_token";

        await _secretStore.SetSecretAsync(refreshPath, refreshToken, ct);
        if (!string.IsNullOrWhiteSpace(accessToken))
            await _secretStore.SetSecretAsync(accessPath, accessToken, ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            INSERT INTO linked_accounts (
                id, provider, email, display_name, external_account_id, scope,
                refresh_token_secret_path, access_token_secret_path, is_active, created_at, updated_at, last_linked_at
            )
            VALUES (
                @id, @provider, @email, @displayName, @externalAccountId, @scope,
                @refreshPath, @accessPath, TRUE, NOW(), NOW(), NOW()
            )
            ON CONFLICT (provider, email)
            DO UPDATE SET
                display_name = EXCLUDED.display_name,
                external_account_id = EXCLUDED.external_account_id,
                scope = EXCLUDED.scope,
                refresh_token_secret_path = EXCLUDED.refresh_token_secret_path,
                access_token_secret_path = EXCLUDED.access_token_secret_path,
                is_active = TRUE,
                updated_at = NOW(),
                last_linked_at = NOW();
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("provider", ToProviderValue(provider));
        cmd.Parameters.AddWithValue("email", normalizedEmail);
        cmd.Parameters.AddWithValue("displayName", (object?)displayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("externalAccountId", (object?)externalAccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("scope", (object?)scope ?? DBNull.Value);
        cmd.Parameters.AddWithValue("refreshPath", refreshPath);
        cmd.Parameters.AddWithValue("accessPath", (object?)accessPath ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Linked OAuth account {Email} for provider {Provider}", normalizedEmail, provider);
        return (await GetByProviderAndEmailAsync(provider, normalizedEmail, ct))!;
    }

    public async Task<IReadOnlyList<LinkedAccount>> GetByProviderAsync(OAuthProviderType provider, CancellationToken ct = default)
    {
        var list = new List<LinkedAccount>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT id, provider, email, display_name, external_account_id, scope,
                   refresh_token_secret_path, access_token_secret_path, is_active,
                   created_at, updated_at, last_linked_at
            FROM linked_accounts
            WHERE provider = @provider AND is_active = TRUE
            ORDER BY email;
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("provider", ToProviderValue(provider));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadLinkedAccount(reader));
        return list;
    }

    public async Task<IReadOnlyList<LinkedAccount>> GetAllAsync(CancellationToken ct = default)
    {
        var list = new List<LinkedAccount>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT id, provider, email, display_name, external_account_id, scope,
                   refresh_token_secret_path, access_token_secret_path, is_active,
                   created_at, updated_at, last_linked_at
            FROM linked_accounts
            WHERE is_active = TRUE
            ORDER BY provider, email;
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadLinkedAccount(reader));
        return list;
    }

    public async Task<LinkedAccountCredential?> GetCredentialByProviderAndEmailAsync(
        OAuthProviderType provider,
        string email,
        CancellationToken ct = default)
    {
        var account = await GetByProviderAndEmailAsync(provider, email.Trim().ToLowerInvariant(), ct);
        if (account == null)
            return null;

        var refresh = await _secretStore.GetSecretAsync(account.RefreshTokenSecretPath, ct);
        if (string.IsNullOrWhiteSpace(refresh))
            return null;
        var access = account.AccessTokenSecretPath == null
            ? null
            : await _secretStore.GetSecretAsync(account.AccessTokenSecretPath, ct);

        return new LinkedAccountCredential
        {
            Account = account,
            RefreshToken = refresh,
            AccessToken = access
        };
    }

    public async Task DeleteAsync(Guid accountId, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(accountId, ct);
        if (existing == null)
            return;

        if (!string.IsNullOrWhiteSpace(existing.RefreshTokenSecretPath))
            await _secretStore.DeleteSecretAsync(existing.RefreshTokenSecretPath, ct);
        if (!string.IsNullOrWhiteSpace(existing.AccessTokenSecretPath))
            await _secretStore.DeleteSecretAsync(existing.AccessTokenSecretPath!, ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("UPDATE linked_accounts SET is_active = FALSE, updated_at = NOW() WHERE id = @id", connection);
        cmd.Parameters.AddWithValue("id", accountId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<LinkedAccount?> GetByProviderAndEmailAsync(OAuthProviderType provider, string email, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT id, provider, email, display_name, external_account_id, scope,
                   refresh_token_secret_path, access_token_secret_path, is_active,
                   created_at, updated_at, last_linked_at
            FROM linked_accounts
            WHERE provider = @provider AND email = @email AND is_active = TRUE
            LIMIT 1;
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("provider", ToProviderValue(provider));
        cmd.Parameters.AddWithValue("email", email);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return ReadLinkedAccount(reader);
        return null;
    }

    private async Task<LinkedAccount?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT id, provider, email, display_name, external_account_id, scope,
                   refresh_token_secret_path, access_token_secret_path, is_active,
                   created_at, updated_at, last_linked_at
            FROM linked_accounts
            WHERE id = @id
            LIMIT 1;
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return ReadLinkedAccount(reader);
        return null;
    }

    private static LinkedAccount ReadLinkedAccount(NpgsqlDataReader reader)
    {
        return new LinkedAccount
        {
            Id = reader.GetGuid(0),
            Provider = FromProviderValue(reader.GetString(1)),
            Email = reader.GetString(2),
            DisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
            ExternalAccountId = reader.IsDBNull(4) ? null : reader.GetString(4),
            Scope = reader.IsDBNull(5) ? null : reader.GetString(5),
            RefreshTokenSecretPath = reader.GetString(6),
            AccessTokenSecretPath = reader.IsDBNull(7) ? null : reader.GetString(7),
            IsActive = reader.GetBoolean(8),
            CreatedAt = reader.GetDateTime(9),
            UpdatedAt = reader.GetDateTime(10),
            LastLinkedAt = reader.GetDateTime(11)
        };
    }

    private static string ToProviderValue(OAuthProviderType provider) => provider switch
    {
        OAuthProviderType.Google => "google",
        OAuthProviderType.Microsoft => "microsoft",
        _ => provider.ToString().ToLowerInvariant()
    };

    private static OAuthProviderType FromProviderValue(string value) => value.Trim().ToLowerInvariant() switch
    {
        "google" => OAuthProviderType.Google,
        "microsoft" => OAuthProviderType.Microsoft,
        _ => OAuthProviderType.Google
    };
}
