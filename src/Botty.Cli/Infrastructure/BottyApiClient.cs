using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Botty.Cli.Infrastructure;

public class BottyApiClient : IDisposable
{
    private readonly HttpClient _http;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public BottyApiClient(string baseUrl, string? apiKey = null)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        if (apiKey is not null)
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(path.TrimStart('/'), ct);
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    public async Task<T> PostAsync<T>(string path, object? body = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(path.TrimStart('/'), body, JsonOptions, ct);
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    public async Task PostAsync(string path, object? body = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(path.TrimStart('/'), body, JsonOptions, ct);
        await EnsureSuccess(response);
    }

    public async Task<T> PutAsync<T>(string path, object? body = null, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(path.TrimStart('/'), body, JsonOptions, ct);
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(path.TrimStart('/'), ct);
        await EnsureSuccess(response);
    }

    public async IAsyncEnumerable<string> StreamSseAsync(
        string path, object? body = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Accept.Add(new("text/event-stream"));

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (line.StartsWith("data: "))
                yield return line[6..];
        }
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        string message;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = doc.RootElement.TryGetProperty("error", out var err)
                ? err.GetString() ?? body
                : doc.RootElement.TryGetProperty("title", out var title)
                    ? title.GetString() ?? body
                    : body;
        }
        catch
        {
            message = string.IsNullOrWhiteSpace(body)
                ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                : body;
        }

        throw new CliApiException((int)response.StatusCode, message);
    }

    public void Dispose() => _http.Dispose();
}
