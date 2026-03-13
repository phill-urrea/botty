using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Base;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.RegularExpressions;

namespace Botty.Tools.UrlFetch;

/// <summary>
/// Tool for fetching and extracting readable content from URLs.
/// </summary>
public class UrlFetchTool : BaseTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private int _maxContentLength = 50000;
    private int _timeoutSeconds = 30;

    public UrlFetchTool(
        IHttpClientFactory httpClientFactory,
        ILogger<UrlFetchTool> logger)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override string Id => "url_fetch";
    public override string Name => "URL Fetch";
    public override string Description => "Fetch web pages in real time and return readable text content.";

    public override ToolConfigSchema ConfigSchema => new()
    {
        ToolId = Id,
        Fields =
        [
            new ConfigField
            {
                Key = "max_content_length",
                Label = "Max Content Length",
                Description = "Maximum characters to return from fetched pages",
                Type = ConfigFieldType.Number,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "50000"
            },
            new ConfigField
            {
                Key = "timeout_seconds",
                Label = "Timeout Seconds",
                Description = "HTTP timeout for page fetch requests",
                Type = ConfigFieldType.Number,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "30"
            }
        ]
    };

    protected override Task OnInitializeAsync(CancellationToken ct)
    {
        if (int.TryParse(GetConfig("max_content_length"), out var maxLen) && maxLen > 0)
        {
            _maxContentLength = maxLen;
        }

        if (int.TryParse(GetConfig("timeout_seconds"), out var timeout) && timeout > 0)
        {
            _timeoutSeconds = timeout;
        }

        return Task.CompletedTask;
    }

    public override IEnumerable<LlmTool> GetTools()
    {
        return
        [
            new LlmTool
            {
                Name = "url_fetch",
                Description = "Fetch and return readable content from a URL.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "url": { "type": "string", "description": "Absolute URL to fetch (http or https)" }
                    },
                    "required": ["url"]
                }
                """
            }
        ];
    }

    protected override async Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct)
    {
        if (!string.Equals(context.ToolName, "url_fetch", StringComparison.Ordinal))
        {
            return ToolResult.Fail($"Unknown tool: {context.ToolName}");
        }

        var args = ParseArguments<UrlFetchArgs>(context.Arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.Url))
        {
            return ToolResult.Fail("Missing or invalid arguments: 'url' is required.");
        }

        if (!Uri.TryCreate(args.Url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return ToolResult.Fail("Invalid URL. Only absolute http/https URLs are supported.");
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Botty/1.0");

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(uri, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Failed to fetch URL {Url}", uri);
            return ToolResult.Fail($"Request failed: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail("Request timed out.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return ToolResult.Fail($"Page returned status code {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync(ct);
        var title = ExtractTitle(html);
        var content = StripHtml(html);

        if (content.Length > _maxContentLength)
        {
            content = content[.._maxContentLength] + "\n\n[Content truncated]";
        }

        return ToolResult.Ok(ToJson(new
        {
            url = uri.ToString(),
            title,
            content
        }));
    }

    private static string ExtractTitle(string html)
    {
        var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return titleMatch.Success
            ? WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim()
            : string.Empty;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutScripts = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        var withoutStyles = Regex.Replace(withoutScripts, @"<style[^>]*>[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withoutStyles, @"<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private sealed class UrlFetchArgs
    {
        public string? Url { get; set; }
    }
}
