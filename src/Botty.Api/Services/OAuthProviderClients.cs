using Botty.Core.Enums;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Botty.Api.Services;

public record OAuthTokenResult(string AccessToken, string? RefreshToken, string? Scope, string? IdToken);
public record OAuthUserProfile(string Email, string? DisplayName, string? ExternalId);

public interface IOAuthProviderClient
{
    OAuthProviderType Provider { get; }
    string BuildAuthorizationUrl(OAuthProviderConfig config, string state);
    Task<OAuthTokenResult> ExchangeCodeAsync(OAuthProviderConfig config, string code, CancellationToken ct = default);
    Task<OAuthUserProfile> GetUserProfileAsync(string accessToken, CancellationToken ct = default);
}

public class GoogleOAuthProviderClient : IOAuthProviderClient
{
    private readonly HttpClient _httpClient;

    public GoogleOAuthProviderClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public OAuthProviderType Provider => OAuthProviderType.Google;

    public string BuildAuthorizationUrl(OAuthProviderConfig config, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["redirect_uri"] = config.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', config.Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state
        };

        var qs = string.Join("&", query.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return $"https://accounts.google.com/o/oauth2/v2/auth?{qs}";
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(OAuthProviderConfig config, string code, CancellationToken ct = default)
    {
        var body = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["redirect_uri"] = config.RedirectUri,
            ["grant_type"] = "authorization_code"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(body)
        };
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var accessToken = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("OAuth token response missing access_token.");
        var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var refreshEl)
            ? refreshEl.GetString()
            : null;
        var scope = doc.RootElement.TryGetProperty("scope", out var scopeEl)
            ? scopeEl.GetString()
            : null;
        var idToken = doc.RootElement.TryGetProperty("id_token", out var idEl)
            ? idEl.GetString()
            : null;

        return new OAuthTokenResult(accessToken, refreshToken, scope, idToken);
    }

    public async Task<OAuthUserProfile> GetUserProfileAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var email = doc.RootElement.TryGetProperty("email", out var emailEl)
            ? emailEl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("OAuth user profile missing email.");

        var name = doc.RootElement.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var sub = doc.RootElement.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
        return new OAuthUserProfile(email, name, sub);
    }
}
