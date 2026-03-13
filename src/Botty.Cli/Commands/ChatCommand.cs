using System.CommandLine;
using System.Text.Json;
using Botty.Cli.Infrastructure;
using Botty.Cli.Models;
using Spectre.Console;
using AppContext = Botty.Cli.Infrastructure.AppContext;

namespace Botty.Cli.Commands;

public static class ChatCommand
{
    public static Command Create()
    {
        var messageArg = new Argument<string?>("message", () => null, "Message to send");
        var interactiveOption = new Option<bool>("-i", "Interactive mode");
        interactiveOption.AddAlias("--interactive");

        var command = new Command("chat", "Chat with the assistant") { messageArg };
        command.AddOption(interactiveOption);
        command.SetHandler(async (string? message, bool interactive) =>
        {
            var client = AppContext.Client;
            var ct = CancellationToken.None;

            if (interactive || message is null)
            {
                await RunInteractive(client, ct);
                return;
            }

            // One-shot mode
            try
            {
                var response = await client.PostAsync<ChatResponseDto>(
                    "api/chat/simple", new { message, includeMemory = true }, ct);
                Console.WriteLine(response.Content);
            }
            catch (CliApiException ex)
            {
                OutputFormatter.Error(ex.Message);
            }
        }, messageArg, interactiveOption);
        return command;
    }

    private static async Task RunInteractive(BottyApiClient client, CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[bold]Botty Chat[/] [grey](type /quit to exit)[/]");
        AnsiConsole.WriteLine();

        Guid? conversationId = null;

        while (true)
        {
            Console.Write("You> ");
            var input = Console.ReadLine();

            if (input is null or "/quit" or "/exit")
                break;
            if (string.IsNullOrWhiteSpace(input))
                continue;

            Console.Write("Botty> ");

            try
            {
                var messages = new[]
                {
                    new { role = "user", content = input }
                };

                var body = new Dictionary<string, object?>
                {
                    ["messages"] = messages,
                    ["includeMemory"] = true,
                };
                if (conversationId.HasValue)
                    body["conversationId"] = conversationId.Value;

                await foreach (var data in client.StreamSseAsync("api/chat/stream", body, ct))
                {
                    if (data == "[DONE]") break;

                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("conversationId", out var cid) && cid.ValueKind == JsonValueKind.String)
                        {
                            if (Guid.TryParse(cid.GetString(), out var parsed))
                                conversationId = parsed;
                        }

                        if (root.TryGetProperty("delta", out var delta))
                            Console.Write(delta.GetString());
                        else if (root.TryGetProperty("content", out var content))
                            Console.Write(content.GetString());
                    }
                    catch (JsonException)
                    {
                        // Non-JSON SSE data, write as-is
                        Console.Write(data);
                    }
                }
            }
            catch (CliApiException ex)
            {
                OutputFormatter.Error(ex.Message);
            }
            catch (HttpRequestException ex)
            {
                OutputFormatter.Error($"Connection error: {ex.Message}");
            }

            Console.WriteLine();
            Console.WriteLine();
        }
    }
}
