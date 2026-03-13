using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Rendering;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Botty.Cli.Infrastructure;

public static class OutputFormatter
{
    public static void RenderList<T>(
        IEnumerable<T> items,
        string[] columns,
        Func<T, string[]> rowBuilder)
    {
        var format = AppContext.OutputFormat;
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(items, BottyApiClient.JsonOptions));
            return;
        }
        if (format == "yaml")
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            Console.Write(serializer.Serialize(items));
            return;
        }

        var table = new Table().BorderColor(Color.Grey);
        foreach (var col in columns)
            table.AddColumn(new TableColumn(col));
        foreach (var item in items)
            table.AddRow(rowBuilder(item).Select(v => (IRenderable)new Text(v)).ToArray());
        AnsiConsole.Write(table);
    }

    public static void Render<T>(T item)
    {
        var format = AppContext.OutputFormat;
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(item, BottyApiClient.JsonOptions));
            return;
        }
        if (format == "yaml")
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            Console.Write(serializer.Serialize(item));
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(item, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    public static void RenderDetail(params (string Key, string Value)[] rows)
    {
        if (AppContext.OutputFormat != "table") return;
        var table = new Table().BorderColor(Color.Grey).HideHeaders();
        table.AddColumn(new TableColumn("Key"));
        table.AddColumn(new TableColumn("Value"));
        foreach (var (key, value) in rows)
            table.AddRow(new Text(key), new Text(value));
        AnsiConsole.Write(table);
    }

    public static void Success(string message)
    {
        if (!AppContext.Quiet)
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
    }

    public static void Error(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(message)}[/]");
    }

    public static string ShortGuid(Guid id) => id.ToString()[..8];

    public static string FormatDate(DateTime? dt) =>
        dt?.ToString("yyyy-MM-dd HH:mm") ?? "-";
}
