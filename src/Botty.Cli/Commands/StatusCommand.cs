using System.CommandLine;
using Botty.Cli.Models;
using Spectre.Console;
using AppContext = Botty.Cli.Infrastructure.AppContext;
using Fmt = Botty.Cli.Infrastructure.OutputFormatter;

namespace Botty.Cli.Commands;

public static class StatusCommand
{
    public static Command Create()
    {
        var command = new Command("status", "Show system status overview");
        command.SetHandler(async () =>
        {
            var client = AppContext.Client;

            var whatsappTask = SafeCall(() => client.GetAsync<WhatsAppStatusDto>("api/whatsapp/status"));
            var channelsTask = SafeCall(() => client.GetAsync<List<ChannelDto>>("api/channels"));
            var boardTask = SafeCall(() => client.GetAsync<KanbanBoardDto>("api/kanban/board"));
            var schedulerTask = SafeCall(() => client.GetAsync<List<ScheduledTaskDto>>("api/scheduler"));
            var skillsTask = SafeCall(() => client.GetAsync<SkillsListResponse>("api/skills"));

            await Task.WhenAll(whatsappTask, channelsTask, boardTask, schedulerTask, skillsTask);

            if (AppContext.OutputFormat != "table")
            {
                Fmt.Render(new
                {
                    whatsapp = whatsappTask.Result,
                    channels = channelsTask.Result,
                    board = boardTask.Result,
                    scheduler = schedulerTask.Result,
                    skills = skillsTask.Result
                });
                return;
            }

            var rule = new Rule("[bold]Botty System Status[/]") { Justification = Justify.Left };
            AnsiConsole.Write(rule);
            AnsiConsole.WriteLine();

            // WhatsApp
            var wa = whatsappTask.Result;
            if (wa is not null)
            {
                var status = wa.Connected ? "[green]Connected[/]" : "[yellow]Disconnected[/]";
                var phone = wa.PhoneNumber is not null ? $" ({wa.PhoneNumber})" : "";
                AnsiConsole.MarkupLine($"  WhatsApp:    {status}{phone}");
            }
            else
                AnsiConsole.MarkupLine("  WhatsApp:    [grey]unavailable[/]");

            // Channels
            var channels = channelsTask.Result;
            if (channels is not null)
            {
                var connected = channels.Count(c => c.IsConnected);
                AnsiConsole.MarkupLine($"  Channels:    [blue]{connected}[/] connected / {channels.Count} total");
            }
            else
                AnsiConsole.MarkupLine("  Channels:    [grey]unavailable[/]");

            // Tasks
            var board = boardTask.Result;
            if (board is not null)
            {
                AnsiConsole.MarkupLine(
                    $"  Tasks:       [blue]{board.ToDo.Count}[/] todo, " +
                    $"[cyan]{board.InProgress.Count}[/] in progress, " +
                    $"[yellow]{board.NeedsApproval.Count}[/] needs approval, " +
                    $"[green]{board.Done.Count}[/] done");
            }
            else
                AnsiConsole.MarkupLine("  Tasks:       [grey]unavailable[/]");

            // Scheduler
            var schedules = schedulerTask.Result;
            if (schedules is not null)
            {
                var active = schedules.Count(s => s.IsActive);
                AnsiConsole.MarkupLine($"  Scheduled:   [blue]{active}[/] active / {schedules.Count} total");
            }
            else
                AnsiConsole.MarkupLine("  Scheduled:   [grey]unavailable[/]");

            // Skills
            var skills = skillsTask.Result;
            if (skills is not null)
            {
                var configured = skills.Skills.Count(s => s.IsConfigured);
                AnsiConsole.MarkupLine($"  Skills:      [blue]{configured}[/] configured / {skills.Count} total");
            }
            else
                AnsiConsole.MarkupLine("  Skills:      [grey]unavailable[/]");

            AnsiConsole.WriteLine();
        });
        return command;
    }

    private static async Task<T?> SafeCall<T>(Func<Task<T>> call) where T : class
    {
        try { return await call(); }
        catch { return null; }
    }
}
