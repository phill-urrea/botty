using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Base;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Botty.Tools.BraveSearch;

/// <summary>
/// Tool for real-time web search using the Brave Search API.
/// </summary>
public class BraveSearchTool : BaseTool
{
    private const string ApiBase = "https://api.search.brave.com/res/v1/web/search";
    private readonly IHttpClientFactory _httpClientFactory;
    private string? _apiKey;
    private int _maxResults = 10;

    public BraveSearchTool(
        IHttpClientFactory httpClientFactory,
        ILogger<BraveSearchTool> logger)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override string Id => "brave_search";
    public override string Name => "Brave Search";
    public override string Description => "Search the web in real time using Brave Search for current information.";

    public override ToolConfigSchema ConfigSchema => new()
    {
        ToolId = Id,
        Fields =
        [
            new ConfigField
            {
                Key = "api_key",
                Label = "API Key",
                Description = "Brave Search API key from https://api-dashboard.search.brave.com",
                Type = ConfigFieldType.Secret,
                IsSensitive = true,
                IsRequired = true
            },
            new ConfigField
            {
                Key = "max_results",
                Label = "Max Results",
                Description = "Maximum number of search results to return per query (default: 10, max: 20)",
                Type = ConfigFieldType.Number,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "10"
            }
        ]
    };

    protected override Task OnInitializeAsync(CancellationToken ct)
    {
        _apiKey = GetConfig("api_key");

        if (int.TryParse(GetConfig("max_results"), out var max) && max > 0)
        {
            _maxResults = Math.Min(max, 20);
        }

        return Task.CompletedTask;
    }

    public override IEnumerable<LlmTool> GetTools()
    {
        return
        [
            new LlmTool
            {
                Name = "brave_web_search",
                Description = "Search the web for current information and return titles, URLs, and snippets.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "query": { "type": "string", "description": "Search query" },
                        "count": { "type": "integer", "description": "Number of results to return (default from config, max 20)" }
                    },
                    "required": ["query"]
                }
                """
            }
        ];
    }

    protected override async Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct)
    {
        if (!string.Equals(context.ToolName, "brave_web_search", StringComparison.Ordinal))
        {
            return ToolResult.Fail($"Unknown tool: {context.ToolName}");
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return ToolResult.Fail("Brave Search API key is not configured.");
        }

        var args = ParseArguments<BraveSearchArgs>(context.Arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.Query))
        {
            return ToolResult.Fail("Missing or invalid arguments: 'query' is required.");
        }

        var count = args.Count ?? _maxResults;
        count = Math.Clamp(count, 1, 20);

        var requestUrl = $"{ApiBase}?q={Uri.EscapeDataString(args.Query.Trim())}&count={count}&extra_snippets=true";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Add("X-Subscription-Token", _apiKey);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Botty", "1.0"));

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(requestUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Brave Search request failed");
            return ToolResult.Fail($"Search request failed: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail("Search request timed out.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            Logger.LogWarning("Brave Search API returned {StatusCode}. Body: {Body}", response.StatusCode, body);
            return ToolResult.Fail($"Search API returned {response.StatusCode}. Check API key and quota.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var items = new List<object>();
        if (doc.RootElement.TryGetProperty("web", out var web) &&
            web.TryGetProperty("results", out var results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in results.EnumerateArray())
            {
                var title = result.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                var url = result.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                var description = result.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;

                var extraSnippets = new List<string>();
                if (result.TryGetProperty("extra_snippets", out var snippets) && snippets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var snippet in snippets.EnumerateArray())
                    {
                        var value = snippet.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            extraSnippets.Add(value);
                        }
                    }
                }

                items.Add(new
                {
                    title,
                    url,
                    description,
                    extraSnippets
                });
            }
        }

        return ToolResult.Ok(ToJson(new
        {
            query = args.Query.Trim(),
            count = items.Count,
            results = items
        }));
    }

    private sealed class BraveSearchArgs
    {
        public string? Query { get; set; }
        public int? Count { get; set; }
    }
}
