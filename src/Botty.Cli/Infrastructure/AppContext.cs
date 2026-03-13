namespace Botty.Cli.Infrastructure;

public static class AppContext
{
    public static BottyApiClient Client { get; set; } = null!;
    public static CliConfig Config { get; set; } = null!;
    public static string OutputFormat { get; set; } = "table";
    public static bool Quiet { get; set; }
    public static bool Verbose { get; set; }
}
