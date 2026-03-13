using System.CommandLine;
using System.Text.Json;
using Botty.Cli.Infrastructure;
using Botty.Cli.Models;
using AppContext = Botty.Cli.Infrastructure.AppContext;
using Fmt = Botty.Cli.Infrastructure.OutputFormatter;

namespace Botty.Cli.Commands;

public static class MemoryCommand
{
    public static Command Create()
    {
        var command = new Command("memory", "Manage memories");
        command.AddCommand(CreateSearch());
        command.AddCommand(CreateShow());
        command.AddCommand(CreateForget());
        command.AddCommand(CreateExport());
        command.AddCommand(CreateStats());
        return command;
    }

    private static Command CreateSearch()
    {
        var queryArg = new Argument<string?>("query", () => null, "Search query");
        var typeOption = new Option<string?>("--type", "Filter by memory type");
        typeOption.AddCompletions("Preference", "Project", "Artifact", "Episode", "Fact", "Relationship");
        var limitOption = new Option<int>("--limit", () => 20, "Max results");

        var cmd = new Command("search", "Search memories") { queryArg };
        cmd.AddOption(typeOption);
        cmd.AddOption(limitOption);
        cmd.SetHandler(async (string? query, string? type, int limit) =>
        {
            var q = $"api/memory/search?limit={limit}";
            if (query is not null) q += $"&query={Uri.EscapeDataString(query)}";
            if (type is not null) q += $"&type={Uri.EscapeDataString(type)}";

            var response = await AppContext.Client.GetAsync<MemorySearchResponse>(q);

            Fmt.RenderList(response.Memories,
                ["Id", "Type", "Content", "Confidence", "Created"],
                m => [
                    Fmt.ShortGuid(m.Id),
                    m.Type,
                    Truncate(m.Content, 60),
                    m.Confidence.ToString("F2"),
                    Fmt.FormatDate(m.CreatedAt)
                ]);
        }, queryArg, typeOption, limitOption);
        return cmd;
    }

    private static Command CreateShow()
    {
        var idArg = new Argument<Guid>("id", "Memory ID");
        var cmd = new Command("show", "Show memory details") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            var memory = await AppContext.Client.GetAsync<MemoryDto>($"api/memory/{id}");
            if (AppContext.OutputFormat != "table")
            {
                Fmt.Render(memory);
                return;
            }
            Fmt.RenderDetail(
                ("Id", memory.Id.ToString()),
                ("Type", memory.Type),
                ("Content", memory.Content),
                ("Confidence", memory.Confidence.ToString("F2")),
                ("Sensitivity", memory.Sensitivity),
                ("Source", memory.Source ?? "-"),
                ("Created", Fmt.FormatDate(memory.CreatedAt)),
                ("Updated", Fmt.FormatDate(memory.UpdatedAt))
            );
        }, idArg);
        return cmd;
    }

    private static Command CreateForget()
    {
        var idArg = new Argument<Guid>("id", "Memory ID");
        var cmd = new Command("forget", "Delete a memory") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            await AppContext.Client.DeleteAsync($"api/memory/{id}");
            Fmt.Success($"Memory {Fmt.ShortGuid(id)} deleted");
        }, idArg);
        return cmd;
    }

    private static Command CreateExport()
    {
        var typeOption = new Option<string?>("--type", "Filter by memory type");
        typeOption.AddCompletions("Preference", "Project", "Artifact", "Episode", "Fact", "Relationship");

        var cmd = new Command("export", "Export all memories as JSON");
        cmd.AddOption(typeOption);
        cmd.SetHandler(async (string? type) =>
        {
            var q = "api/memory/search?limit=10000";
            if (type is not null) q += $"&type={Uri.EscapeDataString(type)}";

            var response = await AppContext.Client.GetAsync<MemorySearchResponse>(q);
            Console.WriteLine(JsonSerializer.Serialize(response.Memories, BottyApiClient.JsonOptions));
        }, typeOption);
        return cmd;
    }

    private static Command CreateStats()
    {
        var cmd = new Command("stats", "Show memory statistics");
        cmd.SetHandler(async () =>
        {
            var stats = await AppContext.Client.GetAsync<MemoryStatsResponse>("api/memory/stats");
            if (AppContext.OutputFormat != "table")
            {
                Fmt.Render(stats);
                return;
            }
            var rows = new List<(string, string)> { ("Total", stats.TotalCount.ToString()) };
            foreach (var (type, count) in stats.ByType)
                rows.Add(($"  {type}", count.ToString()));
            Fmt.RenderDetail([.. rows]);
        });
        return cmd;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";
}
