using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Base;
using Microsoft.Extensions.Logging;

namespace Botty.Tools.Browser;

/// <summary>
/// Tool for browser automation using Playwright. Provides a snapshot→act loop:
/// see the page as an accessibility tree, then interact with elements by ref.
/// </summary>
public class BrowserTool : BaseTool
{
    private readonly BrowserSession _session;

    public BrowserTool(BrowserSession session, ILogger<BrowserTool> logger)
        : base(logger)
    {
        _session = session;
    }

    public override string Id => "browser";
    public override string Name => "Browser";
    public override string Description => "Browse web pages, interact with elements, and extract content using a headless browser.";

    public override ToolConfigSchema ConfigSchema => new()
    {
        ToolId = Id,
        Fields =
        [
            new ConfigField
            {
                Key = "headless",
                Label = "Headless Mode",
                Description = "Run browser without visible window (default: true)",
                Type = ConfigFieldType.Boolean,
                IsRequired = false,
                DefaultValue = "true"
            },
            new ConfigField
            {
                Key = "default_timeout_seconds",
                Label = "Default Timeout",
                Description = "Default timeout for browser operations in seconds (default: 30)",
                Type = ConfigFieldType.Number,
                IsRequired = false,
                DefaultValue = "30"
            },
            new ConfigField
            {
                Key = "max_snapshot_length",
                Label = "Max Snapshot Length",
                Description = "Maximum character length for accessibility snapshots (default: 80000)",
                Type = ConfigFieldType.Number,
                IsRequired = false,
                DefaultValue = "80000"
            },
            new ConfigField
            {
                Key = "enable_javascript_eval",
                Label = "Enable JavaScript Eval",
                Description = "Allow arbitrary JavaScript evaluation in the browser (default: false)",
                Type = ConfigFieldType.Boolean,
                IsRequired = false,
                DefaultValue = "false"
            },
            new ConfigField
            {
                Key = "blocked_urls",
                Label = "Blocked URL Patterns",
                Description = "Comma-separated list of URL patterns to block (e.g., internal metadata endpoints)",
                Type = ConfigFieldType.String,
                IsRequired = false
            }
        ]
    };

    protected override Task OnInitializeAsync(CancellationToken ct)
    {
        if (bool.TryParse(GetConfig("headless"), out var headless))
        {
            _session.Headless = headless;
        }

        if (int.TryParse(GetConfig("default_timeout_seconds"), out var timeout) && timeout > 0)
        {
            _session.DefaultTimeoutSeconds = timeout;
        }

        if (int.TryParse(GetConfig("max_snapshot_length"), out var maxLen) && maxLen > 0)
        {
            _session.MaxSnapshotLength = maxLen;
        }

        if (bool.TryParse(GetConfig("enable_javascript_eval"), out var enableEval))
        {
            _session.EnableJavaScriptEval = enableEval;
        }

        var blockedUrls = GetConfig("blocked_urls");
        if (!string.IsNullOrWhiteSpace(blockedUrls))
        {
            foreach (var pattern in blockedUrls.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _session.BlockedUrlPatterns.Add(pattern);
            }
        }

        return Task.CompletedTask;
    }

    public override IEnumerable<LlmTool> GetTools()
    {
        return
        [
            new LlmTool
            {
                Name = "browser_navigate",
                Description = "Navigate the browser to a URL. Returns the page title and final URL after any redirects.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "url": { "type": "string", "description": "URL to navigate to (must be http or https)" },
                        "timeoutSeconds": { "type": "integer", "description": "Navigation timeout in seconds (default: 30)" }
                    },
                    "required": ["url"]
                }
                """
            },
            new LlmTool
            {
                Name = "browser_snapshot",
                Description = "Capture the current page as an accessibility tree. Each interactive element gets a ref (e.g. e1, e2) that you can use with browser_click and browser_type. IMPORTANT: Always call this before clicking or typing to get current refs.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "interactiveOnly": { "type": "boolean", "description": "If true, only show interactive elements like buttons, links, and inputs (default: false)" }
                    }
                }
                """
            },
            new LlmTool
            {
                Name = "browser_click",
                Description = "Click an element by its ref from a previous browser_snapshot. Call browser_snapshot first to get element refs.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "ref": { "type": "string", "description": "Element ref from snapshot (e.g. 'e1', 'e3')" }
                    },
                    "required": ["ref"]
                }
                """
            },
            new LlmTool
            {
                Name = "browser_type",
                Description = "Type text into an input element by its ref from a previous browser_snapshot. Call browser_snapshot first to get element refs.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "ref": { "type": "string", "description": "Element ref from snapshot (e.g. 'e1', 'e3')" },
                        "text": { "type": "string", "description": "Text to type into the element" },
                        "submit": { "type": "boolean", "description": "Press Enter after typing to submit a form (default: false)" }
                    },
                    "required": ["ref", "text"]
                }
                """
            },
            new LlmTool
            {
                Name = "browser_screenshot",
                Description = "Take a screenshot of the current page. Returns a base64-encoded PNG image.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "fullPage": { "type": "boolean", "description": "Capture the full scrollable page instead of just the viewport (default: false)" }
                    }
                }
                """
            },
            new LlmTool
            {
                Name = "browser_evaluate",
                Description = "Execute JavaScript in the browser and return the result. Must be enabled in tool config.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "expression": { "type": "string", "description": "JavaScript expression to evaluate" }
                    },
                    "required": ["expression"]
                }
                """
            }
        ];
    }

    protected override async Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct)
    {
        switch (context.ToolName)
        {
            case "browser_navigate":
            {
                var args = ParseArguments<NavigateArgs>(context.Arguments);
                if (args == null || string.IsNullOrWhiteSpace(args.Url))
                    return ToolResult.Fail("Missing required argument: 'url'");

                var result = await _session.NavigateAsync(args.Url.Trim(), args.TimeoutSeconds, ct);
                return ToolResult.Ok(ToJson(result));
            }

            case "browser_snapshot":
            {
                var args = ParseArguments<SnapshotArgs>(context.Arguments);
                var interactiveOnly = args?.InteractiveOnly ?? false;

                var result = await _session.SnapshotAsync(interactiveOnly, ct);
                return ToolResult.Ok(ToJson(result));
            }

            case "browser_click":
            {
                var args = ParseArguments<ClickArgs>(context.Arguments);
                if (args == null || string.IsNullOrWhiteSpace(args.Ref))
                    return ToolResult.Fail("Missing required argument: 'ref'");

                var result = await _session.ClickAsync(args.Ref.Trim(), ct);
                return ToolResult.Ok(ToJson(result));
            }

            case "browser_type":
            {
                var args = ParseArguments<TypeArgs>(context.Arguments);
                if (args == null || string.IsNullOrWhiteSpace(args.Ref))
                    return ToolResult.Fail("Missing required argument: 'ref'");
                if (args.Text == null)
                    return ToolResult.Fail("Missing required argument: 'text'");

                var result = await _session.TypeAsync(args.Ref.Trim(), args.Text, args.Submit ?? false, ct);
                return ToolResult.Ok(ToJson(result));
            }

            case "browser_screenshot":
            {
                var args = ParseArguments<ScreenshotArgs>(context.Arguments);
                var fullPage = args?.FullPage ?? false;

                var result = await _session.ScreenshotAsync(fullPage, ct);
                return ToolResult.Ok(ToJson(result));
            }

            case "browser_evaluate":
            {
                var args = ParseArguments<EvaluateArgs>(context.Arguments);
                if (args == null || string.IsNullOrWhiteSpace(args.Expression))
                    return ToolResult.Fail("Missing required argument: 'expression'");

                var result = await _session.EvaluateAsync(args.Expression, ct);
                return ToolResult.Ok(ToJson(result));
            }

            default:
                return ToolResult.Fail($"Unknown tool: {context.ToolName}");
        }
    }
}
