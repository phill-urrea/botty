using Botty.Core.Enums;
using Botty.Core.Interfaces;
using System.Text.Json;

namespace Botty.Api.Services;

/// <summary>
/// Imports legacy Gmail/Calendar account JSON configs into linked account storage.
/// </summary>
public class LegacyOAuthAccountMigrator
{
    private readonly IToolConfigService _toolConfigService;
    private readonly ILinkedAccountService _linkedAccountService;
    private readonly ILogger<LegacyOAuthAccountMigrator> _logger;

    public LegacyOAuthAccountMigrator(
        IToolConfigService toolConfigService,
        ILinkedAccountService linkedAccountService,
        ILogger<LegacyOAuthAccountMigrator> logger)
    {
        _toolConfigService = toolConfigService;
        _linkedAccountService = linkedAccountService;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var migrated = 0;
        migrated += await MigrateToolAccountsAsync("gmail", ct);
        migrated += await MigrateToolAccountsAsync("google-calendar", ct);
        if (migrated > 0)
            _logger.LogInformation("Imported {Count} legacy OAuth account(s) into linked account storage.", migrated);
    }

    private async Task<int> MigrateToolAccountsAsync(string toolId, CancellationToken ct)
    {
        try
        {
            var config = await _toolConfigService.GetConfigAsync(toolId, ct);
            var accountsJson = config.GetValue("accounts");
            if (string.IsNullOrWhiteSpace(accountsJson))
                return 0;

            var accounts = ParseAccounts(accountsJson);
            var migrated = 0;
            foreach (var account in accounts)
            {
                if (string.IsNullOrWhiteSpace(account.Email) || string.IsNullOrWhiteSpace(account.RefreshToken))
                    continue;

                await _linkedAccountService.UpsertAsync(
                    OAuthProviderType.Google,
                    account.Email,
                    account.RefreshToken,
                    account.AccessToken,
                    displayName: null,
                    externalAccountId: null,
                    scope: null,
                    ct);
                migrated++;
            }

            return migrated;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping legacy account import for tool {ToolId}", toolId);
            return 0;
        }
    }

    private static List<LegacyAccountRecord> ParseAccounts(string accountsJson)
    {
        using var doc = JsonDocument.Parse(accountsJson);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<LegacyAccountRecord>>(accountsJson) ?? [];
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("accounts", out var accountsElement) &&
            accountsElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<LegacyAccountRecord>>(accountsElement.GetRawText()) ?? [];
        }

        return [];
    }

    private class LegacyAccountRecord
    {
        public string? Email { get; set; }
        public string? RefreshToken { get; set; }
        public string? AccessToken { get; set; }
    }
}
