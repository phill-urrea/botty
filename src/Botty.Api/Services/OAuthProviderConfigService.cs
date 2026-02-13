using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace Botty.Api.Services;

public class OAuthOptions
{
    public OAuthProviderOptions Google { get; set; } = new();
}

public class OAuthProviderOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }
    public string[]? Scopes { get; set; }
}

public record OAuthProviderConfig(
    OAuthProviderType Provider,
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    IReadOnlyList<string> Scopes);

public interface IOAuthProviderConfigService
{
    Task<OAuthProviderConfig?> GetAsync(OAuthProviderType provider, CancellationToken ct = default);
    Task SaveAsync(
        OAuthProviderType provider,
        string clientId,
        string clientSecret,
        string redirectUri,
        IReadOnlyList<string> scopes,
        CancellationToken ct = default);
}

public class OAuthProviderConfigService : IOAuthProviderConfigService
{
    private readonly ISecretStore _secretStore;
    private readonly OAuthOptions _options;

    public OAuthProviderConfigService(ISecretStore secretStore, IOptions<OAuthOptions> options)
    {
        _secretStore = secretStore;
        _options = options.Value;
    }

    public async Task<OAuthProviderConfig?> GetAsync(OAuthProviderType provider, CancellationToken ct = default)
    {
        var providerKey = ToProviderKey(provider);
        var clientId = await _secretStore.GetSecretAsync($"oauth/providers/{providerKey}/client_id", ct)
            ?? GetFromOptions(provider).ClientId;
        var clientSecret = await _secretStore.GetSecretAsync($"oauth/providers/{providerKey}/client_secret", ct)
            ?? GetFromOptions(provider).ClientSecret;
        var redirectUri = await _secretStore.GetSecretAsync($"oauth/providers/{providerKey}/redirect_uri", ct)
            ?? GetFromOptions(provider).RedirectUri;
        var scopesRaw = await _secretStore.GetSecretAsync($"oauth/providers/{providerKey}/scopes", ct);
        var scopes = ParseScopes(scopesRaw) ?? (GetFromOptions(provider).Scopes?.ToList() ?? DefaultScopes(provider));

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(redirectUri))
            return null;

        return new OAuthProviderConfig(provider, clientId, clientSecret, redirectUri, scopes);
    }

    public async Task SaveAsync(
        OAuthProviderType provider,
        string clientId,
        string clientSecret,
        string redirectUri,
        IReadOnlyList<string> scopes,
        CancellationToken ct = default)
    {
        var providerKey = ToProviderKey(provider);
        await _secretStore.SetSecretAsync($"oauth/providers/{providerKey}/client_id", clientId.Trim(), ct);
        await _secretStore.SetSecretAsync($"oauth/providers/{providerKey}/client_secret", clientSecret.Trim(), ct);
        await _secretStore.SetSecretAsync($"oauth/providers/{providerKey}/redirect_uri", redirectUri.Trim(), ct);
        await _secretStore.SetSecretAsync($"oauth/providers/{providerKey}/scopes", string.Join(' ', scopes), ct);
    }

    private OAuthProviderOptions GetFromOptions(OAuthProviderType provider) => provider switch
    {
        OAuthProviderType.Google => _options.Google,
        _ => new OAuthProviderOptions()
    };

    private static List<string>? ParseScopes(string? scopesRaw)
    {
        if (string.IsNullOrWhiteSpace(scopesRaw))
            return null;
        return scopesRaw
            .Split([' ', ',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> DefaultScopes(OAuthProviderType provider) => provider switch
    {
        OAuthProviderType.Google =>
        [
            "openid",
            "email",
            "profile",
            "https://www.googleapis.com/auth/gmail.readonly",
            "https://www.googleapis.com/auth/gmail.send",
            "https://www.googleapis.com/auth/gmail.modify",
            "https://www.googleapis.com/auth/calendar",
            "https://www.googleapis.com/auth/calendar.events"
        ],
        _ => []
    };

    private static string ToProviderKey(OAuthProviderType provider) => provider switch
    {
        OAuthProviderType.Google => "google",
        OAuthProviderType.Microsoft => "microsoft",
        _ => provider.ToString().ToLowerInvariant()
    };
}
