using System.Collections.Concurrent;

namespace Botty.Api.Services;

public record OAuthStateData(string Provider, string? ReturnUrl, DateTime ExpiresAtUtc);

public interface IOAuthStateStore
{
    string Create(string provider, string? returnUrl, TimeSpan ttl);
    OAuthStateData? Consume(string state);
}

public class OAuthStateStore : IOAuthStateStore
{
    private readonly ConcurrentDictionary<string, OAuthStateData> _states = new();

    public string Create(string provider, string? returnUrl, TimeSpan ttl)
    {
        var state = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        _states[state] = new OAuthStateData(provider, returnUrl, DateTime.UtcNow.Add(ttl));
        return state;
    }

    public OAuthStateData? Consume(string state)
    {
        if (!_states.TryRemove(state, out var data))
            return null;

        if (data.ExpiresAtUtc < DateTime.UtcNow)
            return null;

        return data;
    }
}
