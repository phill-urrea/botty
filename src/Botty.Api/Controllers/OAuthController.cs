using Botty.Api.Services;
using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Botty.Skills.Registry;

namespace Botty.Api.Controllers;

[ApiController]
[Route("api/oauth")]
public class OAuthController : ControllerBase
{
    private readonly IOAuthProviderConfigService _configService;
    private readonly IOAuthStateStore _stateStore;
    private readonly ILinkedAccountService _linkedAccountService;
    private readonly ISkillConfigService _skillConfigService;
    private readonly ISkillRegistry _skillRegistry;
    private readonly Dictionary<OAuthProviderType, IOAuthProviderClient> _providerClients;
    private readonly ILogger<OAuthController> _logger;

    public OAuthController(
        IOAuthProviderConfigService configService,
        IOAuthStateStore stateStore,
        ILinkedAccountService linkedAccountService,
        ISkillConfigService skillConfigService,
        ISkillRegistry skillRegistry,
        IEnumerable<IOAuthProviderClient> providerClients,
        ILogger<OAuthController> logger)
    {
        _configService = configService;
        _stateStore = stateStore;
        _linkedAccountService = linkedAccountService;
        _skillConfigService = skillConfigService;
        _skillRegistry = skillRegistry;
        _providerClients = providerClients.ToDictionary(c => c.Provider);
        _logger = logger;
    }

    [HttpPost("providers/{provider}/start")]
    public async Task<IActionResult> Start(string provider, [FromBody] OAuthStartRequest request, CancellationToken ct)
    {
        var providerType = ParseProvider(provider);
        if (providerType == null)
            return BadRequest(new { error = "Unsupported provider" });

        if (!_providerClients.TryGetValue(providerType.Value, out var client))
            return BadRequest(new { error = "Provider client not configured" });

        var config = await _configService.GetAsync(providerType.Value, ct);
        if (config == null)
            return BadRequest(new { error = $"OAuth provider '{provider}' is not configured" });

        var state = _stateStore.Create(provider, request.ReturnUrl, TimeSpan.FromMinutes(10));
        var url = client.BuildAuthorizationUrl(config, state);
        return Ok(new { authorizationUrl = url, state });
    }

    [HttpGet("providers/{provider}/callback")]
    public async Task<IActionResult> Callback(
        string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { error = $"OAuth provider returned error: {error}" });
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return BadRequest(new { error = "Missing OAuth callback parameters" });

        var providerType = ParseProvider(provider);
        if (providerType == null)
            return BadRequest(new { error = "Unsupported provider" });
        if (!_providerClients.TryGetValue(providerType.Value, out var client))
            return BadRequest(new { error = "Provider client not configured" });

        var stateData = _stateStore.Consume(state);
        if (stateData == null || !string.Equals(stateData.Provider, provider, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Invalid OAuth state" });

        var config = await _configService.GetAsync(providerType.Value, ct);
        if (config == null)
            return BadRequest(new { error = "OAuth provider credentials are not configured" });

        try
        {
            var token = await client.ExchangeCodeAsync(config, code, ct);
            var profile = await client.GetUserProfileAsync(token.AccessToken, ct);

            var refreshToken = token.RefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                var existingCredential = await _linkedAccountService.GetCredentialByProviderAndEmailAsync(
                    providerType.Value,
                    profile.Email,
                    ct);
                refreshToken = existingCredential?.RefreshToken;
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
                return BadRequest(new { error = "Provider did not return a refresh token. Re-consent may be required." });

            var linked = await _linkedAccountService.UpsertAsync(
                providerType.Value,
                profile.Email,
                refreshToken,
                token.AccessToken,
                profile.DisplayName,
                profile.ExternalId,
                token.Scope,
                ct);

            _logger.LogInformation("Linked account {Email} via provider {Provider}", linked.Email, provider);

            if (!string.IsNullOrWhiteSpace(stateData.ReturnUrl))
            {
                var redirectUrl = BuildReturnUrl(stateData.ReturnUrl, linked);
                return Redirect(redirectUrl);
            }

            return Ok(new
            {
                success = true,
                account = new
                {
                    id = linked.Id,
                    provider = linked.Provider.ToString().ToLowerInvariant(),
                    email = linked.Email,
                    displayName = linked.DisplayName
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth callback failed for provider {Provider}", provider);
            return StatusCode(500, new { error = "OAuth callback failed" });
        }
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> ListAccounts(CancellationToken ct)
    {
        var accounts = await _linkedAccountService.GetAllAsync(ct);
        var response = accounts.Select(a => new
        {
            id = a.Id,
            provider = a.Provider.ToString().ToLowerInvariant(),
            email = a.Email,
            displayName = a.DisplayName,
            externalAccountId = a.ExternalAccountId,
            scope = a.Scope,
            createdAt = a.CreatedAt,
            updatedAt = a.UpdatedAt,
            lastLinkedAt = a.LastLinkedAt
        });
        return Ok(new { accounts = response, count = accounts.Count });
    }

    [HttpDelete("accounts/{id:guid}")]
    public async Task<IActionResult> DeleteAccount(Guid id, CancellationToken ct)
    {
        await _linkedAccountService.DeleteAsync(id, ct);
        return Ok(new { success = true });
    }

    [HttpGet("providers/{provider}/config")]
    public async Task<IActionResult> GetProviderConfig(string provider, CancellationToken ct)
    {
        var providerType = ParseProvider(provider);
        if (providerType == null)
            return BadRequest(new { error = "Unsupported provider" });

        var config = await _configService.GetAsync(providerType.Value, ct);
        if (config == null)
            return Ok(new { provider, configured = false });

        return Ok(new
        {
            provider,
            configured = true,
            redirectUri = config.RedirectUri,
            scopes = config.Scopes,
            clientId = Mask(config.ClientId),
            clientSecret = "********"
        });
    }

    [HttpPut("providers/{provider}/config")]
    public async Task<IActionResult> SaveProviderConfig(string provider, [FromBody] SaveOAuthProviderConfigRequest request, CancellationToken ct)
    {
        var providerType = ParseProvider(provider);
        if (providerType == null)
            return BadRequest(new { error = "Unsupported provider" });

        if (string.IsNullOrWhiteSpace(request.ClientId) ||
            string.IsNullOrWhiteSpace(request.ClientSecret) ||
            string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return BadRequest(new { error = "clientId, clientSecret, and redirectUri are required" });
        }

        var scopes = request.Scopes?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList()
            ?? [];

        await _configService.SaveAsync(providerType.Value, request.ClientId, request.ClientSecret, request.RedirectUri, scopes, ct);
        if (providerType.Value == OAuthProviderType.Google)
        {
            await SyncGoogleClientCredentialsIntoSkillsAsync(request.ClientId, request.ClientSecret, ct);
        }
        return Ok(new { success = true });
    }

    private async Task SyncGoogleClientCredentialsIntoSkillsAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        var targetSkills = new[] { "gmail", "google-calendar" };
        foreach (var skillId in targetSkills)
        {
            await _skillConfigService.SetConfigValueAsync(skillId, "client_id", clientId, ct);
            await _skillConfigService.SetConfigValueAsync(skillId, "client_secret", clientSecret, ct);

            var skill = _skillRegistry.Get(skillId);
            if (skill == null)
                continue;
            var config = await _skillConfigService.GetConfigAsync(skillId, ct);
            await skill.InitializeAsync(config, ct);
        }
    }

    private static OAuthProviderType? ParseProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "google" => OAuthProviderType.Google,
        "microsoft" => OAuthProviderType.Microsoft,
        _ => null
    };

    private static string BuildReturnUrl(string returnUrl, Botty.Core.Models.LinkedAccount linked)
    {
        var separator = returnUrl.Contains('?') ? '&' : '?';
        return $"{returnUrl}{separator}oauth=success&provider={linked.Provider.ToString().ToLowerInvariant()}&email={Uri.EscapeDataString(linked.Email)}";
    }

    private static string Mask(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Length <= 8
                ? "********"
                : $"{value[..4]}...{value[^4..]}";
}

public class OAuthStartRequest
{
    public string? ReturnUrl { get; set; }
}

public class SaveOAuthProviderConfigRequest
{
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public required string RedirectUri { get; set; }
    public List<string>? Scopes { get; set; }
}
