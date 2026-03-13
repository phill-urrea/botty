using System.CommandLine;
using System.Text.Json;
using Botty.Cli.Infrastructure;
using Botty.Cli.Models;
using AppContext = Botty.Cli.Infrastructure.AppContext;
using Fmt = Botty.Cli.Infrastructure.OutputFormatter;

namespace Botty.Cli.Commands;

public static class SkillsCommand
{
    public static Command Create()
    {
        var command = new Command("skills", "Manage skills");
        command.AddCommand(CreateList());
        command.AddCommand(CreateShow());
        command.AddCommand(CreateConfig());
        command.AddCommand(CreateExecute());
        return command;
    }

    private static Command CreateList()
    {
        var cmd = new Command("list", "List all skills");
        cmd.SetHandler(async () =>
        {
            var response = await AppContext.Client.GetAsync<SkillsListResponse>("api/skills");
            Fmt.RenderList(response.Skills,
                ["Id", "Name", "Tools", "Configured"],
                s => [s.Id, s.Name, s.ToolCount.ToString(), s.IsConfigured ? "Yes" : "No"]);
        });
        return cmd;
    }

    private static Command CreateShow()
    {
        var idArg = new Argument<string>("id", "Skill ID");
        var cmd = new Command("show", "Show skill details") { idArg };
        cmd.SetHandler(async (string id) =>
        {
            var skill = await AppContext.Client.GetAsync<SkillDetailResponse>($"api/skills/{id}");
            if (AppContext.OutputFormat != "table")
            {
                Fmt.Render(skill);
                return;
            }
            var rows = new List<(string, string)>
            {
                ("Id", skill.Id),
                ("Name", skill.Name),
                ("Description", skill.Description ?? "-"),
                ("Configured", skill.IsConfigured ? "Yes" : "No"),
            };
            if (skill.ValidationErrors is { Count: > 0 })
                rows.Add(("Errors", string.Join(", ", skill.ValidationErrors)));
            Fmt.RenderDetail([.. rows]);

            if (skill.Tools.Count > 0)
            {
                Console.WriteLine();
                Fmt.RenderList(skill.Tools,
                    ["Tool", "Description"],
                    tool => [tool.Name, tool.Description ?? "-"]);
            }
        }, idArg);
        return cmd;
    }

    private static Command CreateConfig()
    {
        var idArg = new Argument<string>("id", "Skill ID");
        var cmd = new Command("config", "Show skill configuration") { idArg };
        cmd.SetHandler(async (string id) =>
        {
            var config = await AppContext.Client.GetAsync<SkillConfigResponse>($"api/skills/{id}/config");
            if (AppContext.OutputFormat != "table")
            {
                Fmt.Render(config);
                return;
            }
            var rows = new List<(string, string)> { ("Skill", config.SkillId) };
            foreach (var (key, value) in config.Values)
                rows.Add((key, value?.ToString() ?? "-"));
            Fmt.RenderDetail([.. rows]);
        }, idArg);
        return cmd;
    }

    private static Command CreateExecute()
    {
        var idArg = new Argument<string>("id", "Skill ID");
        var toolOption = new Option<string>("--tool", "Tool name to execute") { IsRequired = true };
        var argsOption = new Option<string?>("--args", "Tool arguments as JSON");

        var cmd = new Command("execute", "Execute a skill tool") { idArg };
        cmd.AddOption(toolOption);
        cmd.AddOption(argsOption);
        cmd.SetHandler(async (string id, string tool, string? argsJson) =>
        {
            var body = new Dictionary<string, object?> { ["toolName"] = tool };
            if (argsJson is not null)
                body["arguments"] = JsonSerializer.Deserialize<JsonElement>(argsJson);

            var result = await AppContext.Client.PostAsync<ExecuteToolResponse>($"api/skills/{id}/execute", body);
            if (result.Success)
            {
                if (result.Result is not null)
                    Console.WriteLine(result.Result);
                else
                    Fmt.Success("Tool executed successfully");
            }
            else
            {
                Fmt.Error(result.Error ?? "Execution failed");
            }
        }, idArg, toolOption, argsOption);
        return cmd;
    }
}
