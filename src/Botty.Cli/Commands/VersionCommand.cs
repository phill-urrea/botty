using System.CommandLine;
using System.Reflection;

namespace Botty.Cli.Commands;

public static class VersionCommand
{
    public static Command Create()
    {
        var command = new Command("version", "Show version info");
        command.SetHandler(() =>
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"botty v{version?.ToString(3) ?? "0.0.0"}");
        });
        return command;
    }
}
