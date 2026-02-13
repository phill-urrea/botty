using System.Text;
using System.Text.Json;
using Botty.Hooks.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Hooks.Actions;

/// <summary>
/// Sends an HTTP request to a URL (e.g. webhook callback) with payload from context.
/// </summary>
public class HttpCallbackAction : IHookAction
{
    public string Type => "http_callback";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpCallbackAction> _logger;

    public HttpCallbackAction(IHttpClientFactory httpClientFactory, ILogger<HttpCallbackAction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(JsonDocument config, HookContext context, CancellationToken ct = default)
    {
        var root = config.RootElement;
        var url = root.GetProperty("url").GetString();
        if (string.IsNullOrEmpty(url))
            return new ActionResult { Success = false, Error = "Missing url in action config" };

        var method = root.TryGetProperty("method", out var mEl) ? mEl.GetString() ?? "POST" : "POST";
        var client = _httpClientFactory.CreateClient("HookHttpCallback");
        var content = new StringContent(context.Payload.RootElement.GetRawText(), Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = content };

        try
        {
            var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HttpCallback {Url} returned {Status}: {Body}", url, response.StatusCode, body);
                return new ActionResult { Success = false, Error = $"{response.StatusCode}: {body}" };
            }
            return new ActionResult { Success = true, Output = body };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HttpCallback {Url} failed", url);
            return new ActionResult { Success = false, Error = ex.Message };
        }
    }
}
