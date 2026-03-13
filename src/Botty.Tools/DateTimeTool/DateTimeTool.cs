using System.Text.Json.Serialization;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Base;
using Microsoft.Extensions.Logging;

namespace Botty.Tools.DateTimeTool;

/// <summary>
/// Tool that provides the current date and time to the LLM.
/// This gives the agent deterministic access to the current timestamp.
/// </summary>
public class DateTimeTool : BaseTool
{
    public DateTimeTool(ILogger<DateTimeTool> logger) : base(logger) { }

    public override string Id => "datetime";
    public override string Name => "Date & Time";
    public override string Description => "Get the current date and time in any timezone.";

    public override ToolConfigSchema ConfigSchema => new()
    {
        ToolId = Id,
        Fields = []
    };

    protected override Task OnInitializeAsync(CancellationToken ct) => Task.CompletedTask;

    public override IEnumerable<LlmTool> GetTools()
    {
        yield return new LlmTool
        {
            Name = "get_current_datetime",
            Description = """
                Get the current date and time. Always call this tool when you need to know what time or date it is,
                or when the user asks about scheduling relative to "now" (e.g. "in 5 minutes", "tomorrow at 9am").
                Returns UTC time and optionally a converted timezone.
                """,
            ParametersSchema = """
            {
                "type": "object",
                "properties": {
                    "timezone": {
                        "type": "string",
                        "description": "Optional IANA timezone to convert to (e.g. 'Australia/Sydney', 'America/New_York', 'Europe/London'). If omitted, returns UTC only."
                    }
                }
            }
            """
        };
    }

    protected override Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct)
    {
        return context.ToolName switch
        {
            "get_current_datetime" => Task.FromResult(GetCurrentDateTime(context)),
            _ => Task.FromResult(ToolResult.Fail($"Unknown tool: {context.ToolName}"))
        };
    }

    private ToolResult GetCurrentDateTime(ToolContext context)
    {
        var args = ParseArguments<GetDateTimeArgs>(context.Arguments);
        var utcNow = System.DateTime.UtcNow;

        var result = new Dictionary<string, object>
        {
            ["utc"] = utcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            ["utc_iso8601"] = utcNow.ToString("o"),
            ["unix_timestamp"] = new DateTimeOffset(utcNow).ToUnixTimeSeconds(),
            ["day_of_week"] = utcNow.DayOfWeek.ToString()
        };

        if (!string.IsNullOrWhiteSpace(args?.Timezone))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(args.Timezone.Trim());
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
                result["local"] = localTime.ToString("yyyy-MM-dd HH:mm:ss");
                result["local_iso8601"] = new DateTimeOffset(localTime, tz.GetUtcOffset(localTime)).ToString("o");
                result["timezone"] = args.Timezone.Trim();
                result["utc_offset"] = tz.GetUtcOffset(localTime).ToString();
                result["local_day_of_week"] = localTime.DayOfWeek.ToString();
            }
            catch (TimeZoneNotFoundException)
            {
                result["timezone_error"] = $"Unknown timezone: {args.Timezone}. Use IANA format like 'Australia/Sydney'.";
            }
        }

        return ToolResult.Ok(ToJson(result));
    }

    private sealed class GetDateTimeArgs
    {
        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }
}
