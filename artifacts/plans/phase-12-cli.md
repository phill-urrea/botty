# Phase 12: Command-Line Interface

## Overview

Create a comprehensive CLI for Botty that enables power users to manage channels, tasks, memory, plugins, and configuration from the terminal. The CLI will communicate with the Botty API and provide a faster alternative to the Admin UI for common operations.

### Goals

- Build a `Botty.Cli` project using `System.CommandLine`
- Implement commands for all major system functions
- Support shell completion (bash, zsh, PowerShell)
- Enable direct chat with the assistant from terminal
- Provide scripting-friendly output formats (JSON, table)

### Non-Goals

- Full replacement for Admin UI (visual features like Kanban drag-drop)
- Interactive TUI dashboard
- Daemon/background service mode (use API directly)

## Architecture

```mermaid
flowchart LR
    subgraph cli [Botty CLI]
        Parser[Command Parser]
        Client[API Client]
        Output[Output Formatter]
    end
    
    subgraph api [Botty API]
        Channels[/api/channels]
        Tasks[/api/tasks]
        Memory[/api/memory]
        Hooks[/api/hooks]
        Config[/api/config]
        Chat[/api/chat]
    end
    
    User[User] --> Parser
    Parser --> Client
    Client --> Channels
    Client --> Tasks
    Client --> Memory
    Client --> Hooks
    Client --> Config
    Client --> Chat
    Client --> Output
    Output --> User
```

## Command Structure

```
botty
├── channels
│   ├── list                    # List all channels
│   ├── status [id]             # Get channel status
│   ├── connect <id>            # Connect a channel
│   ├── disconnect <id>         # Disconnect a channel
│   └── send <id> <chat> <msg>  # Send message
│
├── tasks
│   ├── list                    # List tasks (filterable)
│   ├── show <id>               # Show task details
│   ├── create                  # Create new task
│   ├── approve <id>            # Approve task
│   ├── reject <id>             # Reject task
│   ├── move <id> <lane>        # Move task to lane
│   └── assign <id> <assignee>  # Assign to user/assistant
│
├── memory
│   ├── search <query>          # Search memories
│   ├── show <id>               # Show memory details
│   ├── forget <id>             # Delete memory
│   ├── export                  # Export all memories
│   └── import                  # Import memories
│
├── hooks
│   ├── list                    # List all hooks
│   ├── show <id>               # Show hook details
│   ├── create                  # Create new hook
│   ├── enable <id>             # Enable hook
│   ├── disable <id>            # Disable hook
│   ├── test <id>               # Test hook
│   └── logs <id>               # View execution logs
│
├── schedule
│   ├── list                    # List scheduled tasks
│   ├── show <id>               # Show schedule details
│   ├── create                  # Create scheduled task
│   ├── pause <id>              # Pause schedule
│   ├── resume <id>             # Resume schedule
│   ├── trigger <id>            # Run immediately
│   └── delete <id>             # Delete schedule
│
├── skills
│   ├── list                    # List all skills
│   ├── show <id>               # Show skill details
│   ├── config <id>             # Get/set skill config
│   └── execute <id> <tool>     # Execute skill tool
│
├── plugins
│   ├── list                    # List installed plugins
│   ├── install <path>          # Install plugin
│   ├── uninstall <id>          # Remove plugin
│   ├── enable <id>             # Enable plugin
│   └── disable <id>            # Disable plugin
│
├── config
│   ├── get <key>               # Get config value
│   ├── set <key> <value>       # Set config value
│   ├── list                    # List all config
│   └── edit                    # Open in $EDITOR
│
├── llm
│   ├── providers               # List LLM providers
│   ├── status                  # Provider health
│   └── test <provider>         # Test provider
│
├── chat <message>              # Chat with assistant
│
├── status                      # System status overview
│
└── version                     # Show version info
```

## Interface Definitions

### CLI Options Model

```csharp
namespace Botty.Cli;

public class GlobalOptions
{
    [Option("--api-url", Description = "Botty API URL")]
    public string ApiUrl { get; set; } = "http://localhost:5001";
    
    [Option("--api-key", Description = "API key for authentication")]
    public string? ApiKey { get; set; }
    
    [Option("-o|--output", Description = "Output format")]
    public OutputFormat Output { get; set; } = OutputFormat.Table;
    
    [Option("-q|--quiet", Description = "Suppress non-essential output")]
    public bool Quiet { get; set; }
    
    [Option("-v|--verbose", Description = "Verbose output")]
    public bool Verbose { get; set; }
}

public enum OutputFormat
{
    Table,
    Json,
    Yaml,
    Plain
}
```

### API Client Interface

```csharp
public interface IBottyApiClient
{
    // Channels
    Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken ct = default);
    Task<ChannelStatus> GetChannelStatusAsync(string channelId, CancellationToken ct = default);
    Task ConnectChannelAsync(string channelId, CancellationToken ct = default);
    Task DisconnectChannelAsync(string channelId, CancellationToken ct = default);
    Task<SendResult> SendMessageAsync(string channelId, string chatId, string message, CancellationToken ct = default);
    
    // Tasks
    Task<IEnumerable<KanbanTask>> GetTasksAsync(TaskFilter? filter = null, CancellationToken ct = default);
    Task<KanbanTask> GetTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<KanbanTask> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default);
    Task<KanbanTask> ApproveTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<KanbanTask> RejectTaskAsync(Guid taskId, string? reason = null, CancellationToken ct = default);
    
    // Memory
    Task<IEnumerable<Memory>> SearchMemoriesAsync(string query, int limit = 20, CancellationToken ct = default);
    Task<Memory> GetMemoryAsync(Guid memoryId, CancellationToken ct = default);
    Task DeleteMemoryAsync(Guid memoryId, CancellationToken ct = default);
    
    // Chat
    Task<ChatResponse> ChatAsync(string message, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(string message, CancellationToken ct = default);
    
    // Health
    Task<SystemStatus> GetStatusAsync(CancellationToken ct = default);
}
```

## Implementation Tasks

### Task 1: Create Botty.Cli Project

**Files to create:**
- `botty/src/Botty.Cli/Botty.Cli.csproj`
- `botty/src/Botty.Cli/Program.cs`
- `botty/src/Botty.Cli/GlobalOptions.cs`

**Project file:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Botty.Cli</RootNamespace>
    <AssemblyName>botty</AssemblyName>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>botty</ToolCommandName>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
    <PackageReference Include="Spectre.Console" Version="0.49.1" />
    <PackageReference Include="YamlDotNet" Version="15.1.2" />
  </ItemGroup>
</Project>
```

**Program.cs structure:**
```csharp
using System.CommandLine;
using Botty.Cli.Commands;

var rootCommand = new RootCommand("Botty AI Assistant CLI")
{
    new ChannelsCommand(),
    new TasksCommand(),
    new MemoryCommand(),
    new HooksCommand(),
    new ScheduleCommand(),
    new SkillsCommand(),
    new PluginsCommand(),
    new ConfigCommand(),
    new LlmCommand(),
    new ChatCommand(),
    new StatusCommand(),
};

rootCommand.AddGlobalOption(new Option<string>("--api-url", () => "http://localhost:5000"));
rootCommand.AddGlobalOption(new Option<string?>("--api-key"));
rootCommand.AddGlobalOption(new Option<OutputFormat>("-o", () => OutputFormat.Table));

return await rootCommand.InvokeAsync(args);
```

### Task 2: Implement API Client

**Files to create:**
- `botty/src/Botty.Cli/Client/BottyApiClient.cs`
- `botty/src/Botty.Cli/Client/ApiException.cs`

```csharp
public class BottyApiClient : IBottyApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public BottyApiClient(string apiUrl, string? apiKey = null)
    {
        _http = new HttpClient { BaseAddress = new Uri(apiUrl) };
        
        if (apiKey != null)
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
    
    public async Task<IEnumerable<KanbanTask>> GetTasksAsync(TaskFilter? filter, CancellationToken ct)
    {
        var query = filter?.ToQueryString() ?? "";
        var response = await _http.GetAsync($"/api/tasks{query}", ct);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<IEnumerable<KanbanTask>>(_jsonOptions, ct)
            ?? Enumerable.Empty<KanbanTask>();
    }
    
    public async IAsyncEnumerable<string> StreamChatAsync(string message, [EnumeratorCancellation] CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new { message })
        };
        
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line?.StartsWith("data: ") == true)
            {
                yield return line[6..];
            }
        }
    }
}
```

### Task 3: Implement Output Formatters

**Files to create:**
- `botty/src/Botty.Cli/Output/IOutputFormatter.cs`
- `botty/src/Botty.Cli/Output/TableFormatter.cs`
- `botty/src/Botty.Cli/Output/JsonFormatter.cs`
- `botty/src/Botty.Cli/Output/YamlFormatter.cs`

```csharp
public interface IOutputFormatter
{
    void Write<T>(T item);
    void WriteList<T>(IEnumerable<T> items, params string[] columns);
    void WriteError(string message);
    void WriteSuccess(string message);
}

public class TableFormatter : IOutputFormatter
{
    public void WriteList<T>(IEnumerable<T> items, params string[] columns)
    {
        var table = new Table();
        
        foreach (var col in columns)
            table.AddColumn(col);
        
        foreach (var item in items)
        {
            var values = columns.Select(c => GetPropertyValue(item, c)?.ToString() ?? "");
            table.AddRow(values.ToArray());
        }
        
        AnsiConsole.Write(table);
    }
}

public class JsonFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public void Write<T>(T item) =>
        Console.WriteLine(JsonSerializer.Serialize(item, Options));
    
    public void WriteList<T>(IEnumerable<T> items, params string[] _) =>
        Console.WriteLine(JsonSerializer.Serialize(items, Options));
}
```

### Task 4: Implement Channel Commands

**Files to create:**
- `botty/src/Botty.Cli/Commands/ChannelsCommand.cs`

```csharp
public class ChannelsCommand : Command
{
    public ChannelsCommand() : base("channels", "Manage messaging channels")
    {
        AddCommand(new ListChannelsCommand());
        AddCommand(new ChannelStatusCommand());
        AddCommand(new ConnectChannelCommand());
        AddCommand(new DisconnectChannelCommand());
        AddCommand(new SendMessageCommand());
    }
}

public class ListChannelsCommand : Command
{
    public ListChannelsCommand() : base("list", "List all channels")
    {
        this.SetHandler(async (context) =>
        {
            var client = context.GetApiClient();
            var formatter = context.GetFormatter();
            
            var channels = await client.GetChannelsAsync(context.GetCancellationToken());
            
            formatter.WriteList(channels, "Id", "Label", "Status", "ConnectedSince");
        });
    }
}

public class SendMessageCommand : Command
{
    public SendMessageCommand() : base("send", "Send a message")
    {
        var channelArg = new Argument<string>("channel", "Channel ID");
        var chatArg = new Argument<string>("chat", "Chat/conversation ID");
        var messageArg = new Argument<string>("message", "Message text");
        
        AddArgument(channelArg);
        AddArgument(chatArg);
        AddArgument(messageArg);
        
        this.SetHandler(async (channel, chat, message, context) =>
        {
            var client = context.GetApiClient();
            var formatter = context.GetFormatter();
            
            var result = await client.SendMessageAsync(channel, chat, message);
            
            if (result.Success)
                formatter.WriteSuccess($"Message sent (ID: {result.MessageId})");
            else
                formatter.WriteError($"Failed: {result.Error}");
                
        }, channelArg, chatArg, messageArg);
    }
}
```

### Task 5: Implement Task Commands

**Files to create:**
- `botty/src/Botty.Cli/Commands/TasksCommand.cs`

```csharp
public class TasksCommand : Command
{
    public TasksCommand() : base("tasks", "Manage Kanban tasks")
    {
        AddCommand(new ListTasksCommand());
        AddCommand(new ShowTaskCommand());
        AddCommand(new CreateTaskCommand());
        AddCommand(new ApproveTaskCommand());
        AddCommand(new RejectTaskCommand());
        AddCommand(new MoveTaskCommand());
    }
}

public class ListTasksCommand : Command
{
    public ListTasksCommand() : base("list", "List tasks")
    {
        var laneOption = new Option<string?>("--lane", "Filter by lane");
        var assigneeOption = new Option<string?>("--assignee", "Filter by assignee");
        var limitOption = new Option<int>("--limit", () => 20, "Max results");
        
        AddOption(laneOption);
        AddOption(assigneeOption);
        AddOption(limitOption);
        
        this.SetHandler(async (lane, assignee, limit, context) =>
        {
            var client = context.GetApiClient();
            var formatter = context.GetFormatter();
            
            var filter = new TaskFilter { Lane = lane, Assignee = assignee, Limit = limit };
            var tasks = await client.GetTasksAsync(filter);
            
            formatter.WriteList(tasks, "Id", "Title", "Lane", "Assignee", "Priority", "CreatedAt");
            
        }, laneOption, assigneeOption, limitOption);
    }
}

public class ApproveTaskCommand : Command
{
    public ApproveTaskCommand() : base("approve", "Approve a task")
    {
        var idArg = new Argument<Guid>("id", "Task ID");
        AddArgument(idArg);
        
        this.SetHandler(async (id, context) =>
        {
            var client = context.GetApiClient();
            var formatter = context.GetFormatter();
            
            var task = await client.ApproveTaskAsync(id);
            formatter.WriteSuccess($"Task '{task.Title}' approved and moved to Done");
            
        }, idArg);
    }
}
```

### Task 6: Implement Chat Command

**Files to create:**
- `botty/src/Botty.Cli/Commands/ChatCommand.cs`

```csharp
public class ChatCommand : Command
{
    public ChatCommand() : base("chat", "Chat with the assistant")
    {
        var messageArg = new Argument<string>("message", "Message to send");
        var streamOption = new Option<bool>("--stream", "Stream response");
        var interactiveOption = new Option<bool>("-i", "Interactive mode");
        
        AddArgument(messageArg);
        AddOption(streamOption);
        AddOption(interactiveOption);
        
        this.SetHandler(async (message, stream, interactive, context) =>
        {
            var client = context.GetApiClient();
            var ct = context.GetCancellationToken();
            
            if (interactive)
            {
                await RunInteractiveMode(client, ct);
                return;
            }
            
            if (stream)
            {
                await foreach (var chunk in client.StreamChatAsync(message, ct))
                {
                    Console.Write(chunk);
                }
                Console.WriteLine();
            }
            else
            {
                var response = await client.ChatAsync(message, ct);
                Console.WriteLine(response.Content);
            }
            
        }, messageArg, streamOption, interactiveOption);
    }
    
    private static async Task RunInteractiveMode(IBottyApiClient client, CancellationToken ct)
    {
        Console.WriteLine("Botty Chat (Ctrl+C to exit)");
        Console.WriteLine();
        
        while (!ct.IsCancellationRequested)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(input))
                continue;
            
            Console.Write("Botty: ");
            await foreach (var chunk in client.StreamChatAsync(input, ct))
            {
                Console.Write(chunk);
            }
            Console.WriteLine();
            Console.WriteLine();
        }
    }
}
```

### Task 7: Implement Memory Commands

**Files to create:**
- `botty/src/Botty.Cli/Commands/MemoryCommand.cs`

```csharp
public class MemoryCommand : Command
{
    public MemoryCommand() : base("memory", "Manage memories")
    {
        AddCommand(new SearchMemoryCommand());
        AddCommand(new ShowMemoryCommand());
        AddCommand(new ForgetMemoryCommand());
        AddCommand(new ExportMemoryCommand());
    }
}

public class SearchMemoryCommand : Command
{
    public SearchMemoryCommand() : base("search", "Search memories")
    {
        var queryArg = new Argument<string>("query", "Search query");
        var limitOption = new Option<int>("--limit", () => 20, "Max results");
        var typeOption = new Option<string?>("--type", "Filter by memory type");
        
        AddArgument(queryArg);
        AddOption(limitOption);
        AddOption(typeOption);
        
        this.SetHandler(async (query, limit, type, context) =>
        {
            var client = context.GetApiClient();
            var formatter = context.GetFormatter();
            
            var memories = await client.SearchMemoriesAsync(query, limit);
            
            if (type != null)
                memories = memories.Where(m => m.Type == type);
            
            formatter.WriteList(memories, "Id", "Type", "Content", "Confidence", "CreatedAt");
            
        }, queryArg, limitOption, typeOption);
    }
}
```

### Task 8: Add Shell Completion Support

**Files to create:**
- `botty/src/Botty.Cli/Completion/CompletionGenerator.cs`

```csharp
public static class CompletionGenerator
{
    public static void GenerateBashCompletion(TextWriter output)
    {
        output.WriteLine(@"
_botty_completions() {
    local cur prev opts
    COMPREPLY=()
    cur=""${COMP_WORDS[COMP_CWORD]}""
    prev=""${COMP_WORDS[COMP_CWORD-1]}""
    
    if [[ ${COMP_CWORD} -eq 1 ]]; then
        opts=""channels tasks memory hooks schedule skills plugins config llm chat status version""
        COMPREPLY=( $(compgen -W ""${opts}"" -- ${cur}) )
        return 0
    fi
    
    case ""${COMP_WORDS[1]}"" in
        channels)
            opts=""list status connect disconnect send""
            ;;
        tasks)
            opts=""list show create approve reject move assign""
            ;;
        # ... more cases
    esac
    
    COMPREPLY=( $(compgen -W ""${opts}"" -- ${cur}) )
}

complete -F _botty_completions botty
");
    }
    
    public static void GenerateZshCompletion(TextWriter output)
    {
        // Similar for zsh
    }
    
    public static void GeneratePowerShellCompletion(TextWriter output)
    {
        // Similar for PowerShell
    }
}
```

### Task 9: Add Status Command

**Files to create:**
- `botty/src/Botty.Cli/Commands/StatusCommand.cs`

```csharp
public class StatusCommand : Command
{
    public StatusCommand() : base("status", "Show system status")
    {
        this.SetHandler(async (context) =>
        {
            var client = context.GetApiClient();
            var status = await client.GetStatusAsync();
            
            var table = new Table().Title("Botty System Status");
            table.AddColumn("Component");
            table.AddColumn("Status");
            table.AddColumn("Details");
            
            // API
            table.AddRow("API", "[green]Online[/]", $"v{status.Version}");
            
            // Database
            var dbStatus = status.Database.Connected ? "[green]Connected[/]" : "[red]Disconnected[/]";
            table.AddRow("Database", dbStatus, $"{status.Database.MemoryCount} memories");
            
            // Channels
            foreach (var channel in status.Channels)
            {
                var chStatus = channel.Connected ? "[green]Connected[/]" : "[yellow]Disconnected[/]";
                table.AddRow($"Channel: {channel.Id}", chStatus, channel.AccountName ?? "");
            }
            
            // LLM Providers
            foreach (var provider in status.LlmProviders)
            {
                var provStatus = provider.Healthy ? "[green]Healthy[/]" : "[red]Unhealthy[/]";
                table.AddRow($"LLM: {provider.Id}", provStatus, $"{provider.Latency}ms avg");
            }
            
            // Tasks
            table.AddRow("Tasks", "[blue]Info[/]", 
                $"{status.Tasks.PendingApproval} pending approval, {status.Tasks.InProgress} in progress");
            
            AnsiConsole.Write(table);
        });
    }
}
```

## Configuration

### CLI Configuration File

The CLI reads configuration from `~/.botty/config.yaml`:

```yaml
api:
  url: http://localhost:5001
  key: your-api-key

defaults:
  output: table
  
aliases:
  approve: tasks approve
  reject: tasks reject
  ask: chat
```

### Environment Variables

| Variable | Description |
|----------|-------------|
| `BOTTY_API_URL` | API base URL |
| `BOTTY_API_KEY` | API authentication key |
| `BOTTY_OUTPUT` | Default output format |

## Testing Strategy

### Unit Tests

- Command parsing and argument validation
- Output formatter correctness
- Configuration loading

### Integration Tests

- API client against mock server
- End-to-end command execution

### Manual Testing

```bash
# Basic operations
botty status
botty channels list
botty tasks list --lane needs-approval
botty tasks approve <id>

# Chat
botty chat "What's on my calendar today?"
botty chat -i  # Interactive mode

# Output formats
botty tasks list -o json
botty tasks list -o yaml
```

## Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `System.CommandLine` | 2.x | Command-line parsing |
| `Spectre.Console` | 0.49.x | Rich terminal output |
| `YamlDotNet` | 15.x | YAML config/output |

## Distribution

### As .NET Tool

```bash
# Install globally
dotnet tool install --global Botty.Cli

# Or from local build
dotnet pack
dotnet tool install --global --add-source ./nupkg Botty.Cli
```

### As Standalone Executable

```bash
# Publish self-contained
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Success Criteria

- [ ] All major commands implemented (channels, tasks, memory, chat)
- [ ] JSON and table output formats working
- [ ] Interactive chat mode functional
- [ ] Shell completion for bash/zsh
- [ ] Installable as .NET global tool
- [ ] API authentication working
