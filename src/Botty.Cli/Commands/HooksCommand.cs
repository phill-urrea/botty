using System.CommandLine;
using System.Text.Json;
using Botty.Cli.Infrastructure;
using Botty.Cli.Models;
using AppContext = Botty.Cli.Infrastructure.AppContext;
using Fmt = Botty.Cli.Infrastructure.OutputFormatter;

namespace Botty.Cli.Commands;

public static class HooksCommand
{
    public static Command Create()
    {
        var command = new Command("hooks", "Manage hooks");
        command.AddCommand(CreateList());
        command.AddCommand(CreateShow());
        command.AddCommand(CreateNew());
        command.AddCommand(CreateEnable());
        command.AddCommand(CreateDisable());
        command.AddCommand(CreateTest());
        command.AddCommand(CreateLogs());
        return command;
    }

    private static Command CreateList()
    {
        var cmd = new Command("list", "List all hooks");
        cmd.SetHandler(async () =>
        {
            var hooks = await AppContext.Client.GetAsync<List<HookListDto>>("api/hooks");
            Fmt.RenderList(hooks,
                ["Id", "Name", "Trigger", "Action", "Enabled"],
                h => [
                    Fmt.ShortGuid(h.Id),
                    h.Name,
                    h.Trigger,
                    h.ActionType,
                    h.IsEnabled ? "Yes" : "No"
                ]);
        });
        return cmd;
    }

    private static Command CreateShow()
    {
        var idArg = new Argument<Guid>("id", "Hook ID");
        var cmd = new Command("show", "Show hook details") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            var hook = await AppContext.Client.GetAsync<HookDetailDto>($"api/hooks/{id}");
            if (AppContext.OutputFormat != "table")
            {
                Fmt.Render(hook);
                return;
            }
            var rows = new List<(string, string)>
            {
                ("Id", hook.Id.ToString()),
                ("Name", hook.Name),
                ("Description", hook.Description ?? "-"),
                ("Trigger", hook.Trigger),
                ("Action Type", hook.ActionType),
                ("Enabled", hook.IsEnabled ? "Yes" : "No"),
                ("Created By", hook.CreatedBy ?? "-"),
                ("Created", Fmt.FormatDate(hook.CreatedAt)),
            };
            if (hook.ConditionJson is not null)
                rows.Add(("Condition", hook.ConditionJson));
            if (hook.ActionConfigJson is not null)
                rows.Add(("Action Config", hook.ActionConfigJson));
            Fmt.RenderDetail([.. rows]);
        }, idArg);
        return cmd;
    }

    private static Command CreateNew()
    {
        var nameOption = new Option<string>("--name", "Hook name") { IsRequired = true };
        var triggerOption = new Option<string>("--trigger", "Trigger type") { IsRequired = true };
        var actionTypeOption = new Option<string>("--action-type", "Action type") { IsRequired = true };
        var actionConfigOption = new Option<string>("--action-config", "Action config JSON") { IsRequired = true };
        var descOption = new Option<string?>("--description", "Hook description");
        var conditionOption = new Option<string?>("--condition", "Condition JSON");

        var cmd = new Command("create", "Create a new hook");
        cmd.AddOption(nameOption);
        cmd.AddOption(triggerOption);
        cmd.AddOption(actionTypeOption);
        cmd.AddOption(actionConfigOption);
        cmd.AddOption(descOption);
        cmd.AddOption(conditionOption);
        cmd.SetHandler(async (string name, string trigger, string actionType, string actionConfig, string? description, string? condition) =>
        {
            var body = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["trigger"] = trigger,
                ["actionType"] = actionType,
                ["actionConfig"] = JsonSerializer.Deserialize<JsonElement>(actionConfig),
            };
            if (description is not null) body["description"] = description;
            if (condition is not null) body["condition"] = JsonSerializer.Deserialize<JsonElement>(condition);

            var hook = await AppContext.Client.PostAsync<HookDetailDto>("api/hooks", body);
            Fmt.Success($"Hook created: {Fmt.ShortGuid(hook.Id)} - {hook.Name}");
        }, nameOption, triggerOption, actionTypeOption, actionConfigOption, descOption, conditionOption);
        return cmd;
    }

    private static Command CreateEnable()
    {
        var idArg = new Argument<Guid>("id", "Hook ID");
        var cmd = new Command("enable", "Enable a hook") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            await AppContext.Client.PostAsync($"api/hooks/{id}/enable");
            Fmt.Success($"Hook {Fmt.ShortGuid(id)} enabled");
        }, idArg);
        return cmd;
    }

    private static Command CreateDisable()
    {
        var idArg = new Argument<Guid>("id", "Hook ID");
        var cmd = new Command("disable", "Disable a hook") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            await AppContext.Client.PostAsync($"api/hooks/{id}/disable");
            Fmt.Success($"Hook {Fmt.ShortGuid(id)} disabled");
        }, idArg);
        return cmd;
    }

    private static Command CreateTest()
    {
        var idArg = new Argument<Guid>("id", "Hook ID");
        var payloadOption = new Option<string?>("--payload", "Test payload JSON");
        var cmd = new Command("test", "Test a hook") { idArg };
        cmd.AddOption(payloadOption);
        cmd.SetHandler(async (Guid id, string? payload) =>
        {
            object? body = payload is not null
                ? JsonSerializer.Deserialize<JsonElement>(payload)
                : null;
            var result = await AppContext.Client.PostAsync<HookResultDto>($"api/hooks/{id}/test", body);
            if (result.Success)
                Fmt.Success($"Hook test passed{(result.Output is not null ? $": {result.Output}" : "")}");
            else
                Fmt.Error(result.Error ?? "Hook test failed");
        }, idArg, payloadOption);
        return cmd;
    }

    private static Command CreateLogs()
    {
        var idArg = new Argument<Guid>("id", "Hook ID");
        var limitOption = new Option<int>("--limit", () => 50, "Max results");
        var cmd = new Command("logs", "View hook execution logs") { idArg };
        cmd.AddOption(limitOption);
        cmd.SetHandler(async (Guid id, int limit) =>
        {
            var logs = await AppContext.Client.GetAsync<List<HookExecutionDto>>($"api/hooks/{id}/logs?limit={limit}");
            Fmt.RenderList(logs,
                ["Time", "Trigger", "Success", "Duration", "Output/Error"],
                l => [
                    Fmt.FormatDate(l.ExecutedAt),
                    l.Trigger,
                    l.Success ? "Yes" : "No",
                    l.DurationMs.HasValue ? $"{l.DurationMs}ms" : "-",
                    Truncate(l.Output ?? l.Error ?? "-", 50)
                ]);
        }, idArg, limitOption);
        return cmd;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";
}
