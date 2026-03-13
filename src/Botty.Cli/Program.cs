using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Botty.Cli.Commands;
using Botty.Cli.Infrastructure;
using AppContext = Botty.Cli.Infrastructure.AppContext;

var apiUrlOption = new Option<string?>("--api-url", "Botty API URL");
var apiKeyOption = new Option<string?>("--api-key", "API key for authentication");
var outputOption = new Option<string?>("-o", "Output format: table, json, yaml");
outputOption.AddAlias("--output");
outputOption.AddCompletions("table", "json", "yaml");
var quietOption = new Option<bool>("-q", "Suppress non-essential output");
quietOption.AddAlias("--quiet");
var verboseOption = new Option<bool>("-v", "Verbose output");
verboseOption.AddAlias("--verbose");

var rootCommand = new RootCommand("Botty CLI - manage your Botty assistant")
{
    ChannelsCommand.Create(),
    TasksCommand.Create(),
    MemoryCommand.Create(),
    HooksCommand.Create(),
    ScheduleCommand.Create(),
    SkillsCommand.Create(),
    ChatCommand.Create(),
    StatusCommand.Create(),
    VersionCommand.Create(),
};

rootCommand.AddGlobalOption(apiUrlOption);
rootCommand.AddGlobalOption(apiKeyOption);
rootCommand.AddGlobalOption(outputOption);
rootCommand.AddGlobalOption(quietOption);
rootCommand.AddGlobalOption(verboseOption);

var builder = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .AddMiddleware(async (context, next) =>
    {
        var config = CliConfig.Load();
        var apiUrl = context.ParseResult.GetValueForOption(apiUrlOption) ?? config.ApiUrl;
        var apiKey = context.ParseResult.GetValueForOption(apiKeyOption) ?? config.ApiKey;
        var output = context.ParseResult.GetValueForOption(outputOption) ?? config.OutputFormat;

        AppContext.Config = config;
        AppContext.Client = new BottyApiClient(apiUrl, apiKey);
        AppContext.OutputFormat = output;
        AppContext.Quiet = context.ParseResult.GetValueForOption(quietOption);
        AppContext.Verbose = context.ParseResult.GetValueForOption(verboseOption);

        try
        {
            await next(context);
        }
        finally
        {
            AppContext.Client.Dispose();
        }
    });

var parser = builder.Build();
return await parser.InvokeAsync(args);
