using System.CommandLine;
using Botty.Cli.Infrastructure;
using Botty.Cli.Models;
using AppContext = Botty.Cli.Infrastructure.AppContext;
using Fmt = Botty.Cli.Infrastructure.OutputFormatter;

namespace Botty.Cli.Commands;

public static class ChannelsCommand
{
    public static Command Create()
    {
        var command = new Command("channels", "Manage messaging channels");
        command.AddCommand(CreateList());
        command.AddCommand(CreateStatus());
        command.AddCommand(CreateConnect());
        command.AddCommand(CreateDisconnect());
        command.AddCommand(CreateSend());
        return command;
    }

    private static Command CreateList()
    {
        var cmd = new Command("list", "List all channels");
        cmd.SetHandler(async () =>
        {
            var channels = await AppContext.Client.GetAsync<List<ChannelDto>>("api/channels");
            Fmt.RenderList(channels,
                ["Id", "Label", "Connected", "Enabled", "Account"],
                c => [c.Id, c.Label, c.IsConnected ? "Yes" : "No", c.IsEnabled ? "Yes" : "No", c.AccountName ?? "-"]);
        });
        return cmd;
    }

    private static Command CreateStatus()
    {
        var idArg = new Argument<string>("id", "Channel ID");
        var cmd = new Command("status", "Get channel status") { idArg };
        cmd.SetHandler(async (string id) =>
        {
            var status = await AppContext.Client.GetAsync<ChannelStatusDto>($"api/channels/{id}/status");
            if (AppContext.OutputFormat != "table")
            {
                Fmt.Render(status);
                return;
            }
            var rows = new List<(string, string)>
            {
                ("Channel", status.ChannelId),
                ("Connected", status.IsConnected ? "Yes" : "No"),
                ("Account", status.AccountName ?? "-"),
                ("Account ID", status.AccountId ?? "-"),
                ("Connected Since", Fmt.FormatDate(status.ConnectedSince)),
            };
            if (status.Error is not null)
                rows.Add(("Error", status.Error));
            Fmt.RenderDetail([.. rows]);
        }, idArg);
        return cmd;
    }

    private static Command CreateConnect()
    {
        var idArg = new Argument<string>("id", "Channel ID");
        var cmd = new Command("connect", "Connect a channel") { idArg };
        cmd.SetHandler(async (string id) =>
        {
            try
            {
                await AppContext.Client.PostAsync<ChannelStatusDto>($"api/channels/{id}/connect");
                Fmt.Success($"Channel '{id}' connected");
            }
            catch (CliApiException ex)
            {
                Fmt.Error(ex.Message);
            }
        }, idArg);
        return cmd;
    }

    private static Command CreateDisconnect()
    {
        var idArg = new Argument<string>("id", "Channel ID");
        var cmd = new Command("disconnect", "Disconnect a channel") { idArg };
        cmd.SetHandler(async (string id) =>
        {
            try
            {
                await AppContext.Client.PostAsync($"api/channels/{id}/disconnect");
                Fmt.Success($"Channel '{id}' disconnected");
            }
            catch (CliApiException ex)
            {
                Fmt.Error(ex.Message);
            }
        }, idArg);
        return cmd;
    }

    private static Command CreateSend()
    {
        var idArg = new Argument<string>("id", "Channel ID");
        var toOption = new Option<string>("--to", "Chat/recipient ID") { IsRequired = true };
        var bodyOption = new Option<string>("--body", "Message text") { IsRequired = true };
        var cmd = new Command("send", "Send a message through a channel") { idArg };
        cmd.AddOption(toOption);
        cmd.AddOption(bodyOption);
        cmd.SetHandler(async (string id, string to, string body) =>
        {
            try
            {
                var result = await AppContext.Client.PostAsync<SendResultDto>(
                    $"api/channels/{id}/send", new { to, body });
                if (result.Success)
                    Fmt.Success($"Message sent (ID: {result.MessageId})");
                else
                    Fmt.Error(result.Error ?? "Send failed");
            }
            catch (CliApiException ex)
            {
                Fmt.Error(ex.Message);
            }
        }, idArg, toOption, bodyOption);
        return cmd;
    }
}
