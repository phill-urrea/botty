using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Base;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Botty.Tools.Scripting;

/// <summary>
/// Tool for creating and executing persistent script-backed tools.
/// </summary>
public partial class ScriptTool : BaseTool
{
    private const string ScriptCreateTool = "script_create";
    private const string ScriptListTool = "script_list";
    private const string ScriptReadTool = "script_read";
    private const string ScriptEditTool = "script_edit";
    private const string ScriptDeleteTool = "script_delete";

    private static readonly HashSet<string> ManagementTools =
    [
        ScriptCreateTool,
        ScriptListTool,
        ScriptReadTool,
        ScriptEditTool,
        ScriptDeleteTool
    ];

    private static readonly HashSet<string> AllowedInterpreters =
    [
        "/bin/bash",
        "bash",
        "python3",
        "node"
    ];

    [GeneratedRegex("^[a-z][a-z0-9_]{2,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptNameRegex();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly ConcurrentDictionary<string, ScriptManifest> _scripts = new(StringComparer.Ordinal);

    private string _workspaceDirectory = "/app/workspace/scripts";
    private int _maxOutputLength = 50000;

    public ScriptTool(ILogger<ScriptTool> logger)
        : base(logger)
    {
    }

    public override string Id => "scripting";
    public override string Name => "Scripting";
    public override string Description => "Create persistent scripts that become runtime tools.";

    public override ToolConfigSchema ConfigSchema => new()
    {
        ToolId = Id,
        Fields =
        [
            new ConfigField
            {
                Key = "workspace_directory",
                Label = "Workspace Directory",
                Description = "Directory where persistent script tools are stored.",
                Type = ConfigFieldType.String,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "/app/workspace/scripts"
            },
            new ConfigField
            {
                Key = "max_output_length",
                Label = "Max Output Length",
                Description = "Maximum stdout/stderr characters captured per script execution.",
                Type = ConfigFieldType.Number,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "50000"
            }
        ]
    };

    protected override Task OnInitializeAsync(CancellationToken ct)
    {
        var configuredWorkspace = GetConfig("workspace_directory");
        if (!string.IsNullOrWhiteSpace(configuredWorkspace))
        {
            _workspaceDirectory = configuredWorkspace.Trim();
        }

        if (int.TryParse(GetConfig("max_output_length"), out var maxOutput) && maxOutput > 0)
        {
            _maxOutputLength = maxOutput;
        }

        Directory.CreateDirectory(_workspaceDirectory);
        return ScanScriptsAsync(ct);
    }

    public override IEnumerable<LlmTool> GetTools()
    {
        var tools = new List<LlmTool>
        {
            new()
            {
                Name = ScriptCreateTool,
                Description = "Create a persistent script tool with metadata and source code.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string", "description": "Tool name in snake_case (3-50 chars)." },
                        "description": { "type": "string", "description": "What the tool does." },
                        "interpreter": { "type": "string", "description": "Interpreter to execute (bash, /bin/bash, python3, node)." },
                        "entrypoint": { "type": "string", "description": "Entrypoint filename (e.g. run.sh, main.py)." },
                        "content": { "type": "string", "description": "Script source code for the entrypoint file." },
                        "parameters": { "type": "object", "description": "JSON Schema object for tool parameters." },
                        "timeout_seconds": { "type": "integer", "description": "Execution timeout in seconds (default: 30)." }
                    },
                    "required": ["name", "description", "interpreter", "entrypoint", "content"]
                }
                """
            },
            new()
            {
                Name = ScriptListTool,
                Description = "List all persistent scripts and their metadata.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {},
                    "required": []
                }
                """
            },
            new()
            {
                Name = ScriptReadTool,
                Description = "Read a script entrypoint file and return its contents.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string", "description": "Script tool name to read." }
                    },
                    "required": ["name"]
                }
                """
            },
            new()
            {
                Name = ScriptEditTool,
                Description = "Overwrite a script entrypoint file and update metadata timestamp.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string", "description": "Script tool name to edit." },
                        "content": { "type": "string", "description": "Replacement script content." }
                    },
                    "required": ["name", "content"]
                }
                """
            },
            new()
            {
                Name = ScriptDeleteTool,
                Description = "Delete a persistent script tool directory.",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string", "description": "Script tool name to delete." }
                    },
                    "required": ["name"]
                }
                """
            }
        };

        var dynamicTools = _scripts.Values
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new LlmTool
            {
                Name = s.Name,
                Description = s.Description,
                ParametersSchema = SerializeParametersSchema(s.Parameters)
            });

        tools.AddRange(dynamicTools);
        return tools;
    }

    protected override Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct)
    {
        return context.ToolName switch
        {
            ScriptCreateTool => CreateScriptAsync(context.Arguments, ct),
            ScriptListTool => ListScriptsAsync(ct),
            ScriptReadTool => ReadScriptAsync(context.Arguments, ct),
            ScriptEditTool => EditScriptAsync(context.Arguments, ct),
            ScriptDeleteTool => DeleteScriptAsync(context.Arguments, ct),
            _ => ExecuteScriptToolAsync(context.ToolName, context.Arguments, ct)
        };
    }

    private async Task<ToolResult> CreateScriptAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<CreateScriptArgs>(arguments);
        if (args == null ||
            string.IsNullOrWhiteSpace(args.Name) ||
            string.IsNullOrWhiteSpace(args.Description) ||
            string.IsNullOrWhiteSpace(args.Interpreter) ||
            string.IsNullOrWhiteSpace(args.Entrypoint) ||
            args.Content == null)
        {
            return ToolResult.Fail("Invalid arguments. Required: name, description, interpreter, entrypoint, content.");
        }

        var name = args.Name.Trim();
        if (!ScriptNameRegex().IsMatch(name))
        {
            return ToolResult.Fail("Invalid script name. Use snake_case and 3-50 characters.");
        }

        if (ManagementTools.Contains(name))
        {
            return ToolResult.Fail($"Script name '{name}' is reserved.");
        }

        var interpreter = args.Interpreter.Trim();
        if (!AllowedInterpreters.Contains(interpreter))
        {
            return ToolResult.Fail("Unsupported interpreter. Allowed: /bin/bash, bash, python3, node.");
        }

        await _mutationLock.WaitAsync(ct);
        try
        {
            if (_scripts.ContainsKey(name))
            {
                return ToolResult.Fail($"Script '{name}' already exists.");
            }

            var scriptDir = GetScriptDirectory(name);
            if (Directory.Exists(scriptDir))
            {
                return ToolResult.Fail($"Directory for script '{name}' already exists.");
            }

            Directory.CreateDirectory(scriptDir);
            var entrypointPath = ResolveEntrypointPath(scriptDir, args.Entrypoint);

            var timeout = args.TimeoutSeconds.GetValueOrDefault(30);
            if (timeout <= 0)
            {
                timeout = 30;
            }

            var manifest = new ScriptManifest
            {
                Name = name,
                Description = args.Description.Trim(),
                Interpreter = interpreter,
                Entrypoint = args.Entrypoint.Trim(),
                Parameters = args.Parameters,
                TimeoutSeconds = timeout,
                CreatedAt = DateTime.UtcNow
            };

            var manifestPath = GetManifestPath(scriptDir);
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, ct);
            await File.WriteAllTextAsync(entrypointPath, args.Content, ct);

            await ScanScriptsAsync(ct);

            return ToolResult.Ok(ToJson(new
            {
                created = true,
                script = manifest.Name,
                directory = scriptDir
            }));
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private Task<ToolResult> ListScriptsAsync(CancellationToken ct)
    {
        var scripts = _scripts.Values
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new
            {
                name = s.Name,
                description = s.Description,
                interpreter = s.Interpreter,
                entrypoint = s.Entrypoint,
                timeoutSeconds = s.TimeoutSeconds,
                createdAt = s.CreatedAt,
                updatedAt = s.UpdatedAt
            });

        return Task.FromResult(ToolResult.Ok(ToJson(new
        {
            scripts,
            count = _scripts.Count
        })));
    }

    private async Task<ToolResult> ReadScriptAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<NamedScriptArgs>(arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.Name))
        {
            return ToolResult.Fail("Invalid arguments. Required: name.");
        }

        var name = args.Name.Trim();
        if (!_scripts.TryGetValue(name, out var manifest))
        {
            return ToolResult.Fail($"Script '{name}' not found.");
        }

        try
        {
            var scriptDir = GetScriptDirectory(manifest.Name);
            var entrypointPath = ResolveEntrypointPath(scriptDir, manifest.Entrypoint);
            var content = await File.ReadAllTextAsync(entrypointPath, ct);

            return ToolResult.Ok(ToJson(new
            {
                name = manifest.Name,
                description = manifest.Description,
                interpreter = manifest.Interpreter,
                entrypoint = manifest.Entrypoint,
                timeoutSeconds = manifest.TimeoutSeconds,
                createdAt = manifest.CreatedAt,
                updatedAt = manifest.UpdatedAt,
                content
            }));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to read script {ScriptName}", name);
            return ToolResult.Fail($"Failed to read script '{name}': {ex.Message}");
        }
    }

    private async Task<ToolResult> EditScriptAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<EditScriptArgs>(arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.Name) || args.Content == null)
        {
            return ToolResult.Fail("Invalid arguments. Required: name, content.");
        }

        var name = args.Name.Trim();

        await _mutationLock.WaitAsync(ct);
        try
        {
            if (!_scripts.TryGetValue(name, out var manifest))
            {
                return ToolResult.Fail($"Script '{name}' not found.");
            }

            var scriptDir = GetScriptDirectory(manifest.Name);
            var entrypointPath = ResolveEntrypointPath(scriptDir, manifest.Entrypoint);
            await File.WriteAllTextAsync(entrypointPath, args.Content, ct);

            manifest.UpdatedAt = DateTime.UtcNow;
            var manifestPath = GetManifestPath(scriptDir);
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                ct);

            await ScanScriptsAsync(ct);

            return ToolResult.Ok(ToJson(new
            {
                updated = true,
                script = name
            }));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to edit script {ScriptName}", name);
            return ToolResult.Fail($"Failed to edit script '{name}': {ex.Message}");
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task<ToolResult> DeleteScriptAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<NamedScriptArgs>(arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.Name))
        {
            return ToolResult.Fail("Invalid arguments. Required: name.");
        }

        var name = args.Name.Trim();
        await _mutationLock.WaitAsync(ct);
        try
        {
            if (!_scripts.ContainsKey(name))
            {
                return ToolResult.Fail($"Script '{name}' not found.");
            }

            var scriptDir = GetScriptDirectory(name);
            if (Directory.Exists(scriptDir))
            {
                Directory.Delete(scriptDir, recursive: true);
            }

            await ScanScriptsAsync(ct);

            return ToolResult.Ok(ToJson(new
            {
                deleted = true,
                script = name
            }));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to delete script {ScriptName}", name);
            return ToolResult.Fail($"Failed to delete script '{name}': {ex.Message}");
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task<ToolResult> ExecuteScriptToolAsync(string toolName, string arguments, CancellationToken ct)
    {
        if (!_scripts.TryGetValue(toolName, out var manifest))
        {
            return ToolResult.Fail($"Unknown tool: {toolName}");
        }

        var scriptDir = GetScriptDirectory(manifest.Name);
        var entrypointPath = ResolveEntrypointPath(scriptDir, manifest.Entrypoint);
        if (!File.Exists(entrypointPath))
        {
            return ToolResult.Fail($"Entrypoint file not found for script '{toolName}'.");
        }

        var timeoutSeconds = manifest.TimeoutSeconds > 0 ? manifest.TimeoutSeconds : 30;
        var processResult = await RunScriptProcessAsync(
            manifest.Interpreter,
            manifest.Entrypoint,
            scriptDir,
            arguments,
            timeoutSeconds,
            ct);

        return ToolResult.Ok(ToJson(new
        {
            script = manifest.Name,
            exitCode = processResult.ExitCode,
            success = processResult.ExitCode == 0 && !processResult.TimedOut,
            timedOut = processResult.TimedOut,
            stdout = processResult.Stdout,
            stderr = processResult.Stderr,
            stdoutTruncated = processResult.StdoutTruncated,
            stderrTruncated = processResult.StderrTruncated
        }));
    }

    private async Task ScanScriptsAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_workspaceDirectory);

        var discovered = new Dictionary<string, ScriptManifest>(StringComparer.Ordinal);
        foreach (var scriptDir in Directory.GetDirectories(_workspaceDirectory))
        {
            ct.ThrowIfCancellationRequested();

            var manifestPath = GetManifestPath(scriptDir);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(manifestPath, ct);
                var manifest = JsonSerializer.Deserialize<ScriptManifest>(json);
                if (manifest == null ||
                    string.IsNullOrWhiteSpace(manifest.Name) ||
                    string.IsNullOrWhiteSpace(manifest.Description) ||
                    string.IsNullOrWhiteSpace(manifest.Interpreter) ||
                    string.IsNullOrWhiteSpace(manifest.Entrypoint))
                {
                    Logger.LogWarning("Skipping malformed manifest at {Path}", manifestPath);
                    continue;
                }

                if (!ScriptNameRegex().IsMatch(manifest.Name))
                {
                    Logger.LogWarning("Skipping manifest with invalid name '{ScriptName}' at {Path}", manifest.Name, manifestPath);
                    continue;
                }

                if (ManagementTools.Contains(manifest.Name))
                {
                    Logger.LogWarning("Skipping manifest with reserved name '{ScriptName}' at {Path}", manifest.Name, manifestPath);
                    continue;
                }

                if (!AllowedInterpreters.Contains(manifest.Interpreter))
                {
                    Logger.LogWarning("Skipping manifest with unsupported interpreter '{Interpreter}' at {Path}", manifest.Interpreter, manifestPath);
                    continue;
                }

                // Ensures entrypoint path is contained inside script directory.
                ResolveEntrypointPath(GetScriptDirectory(manifest.Name), manifest.Entrypoint);

                if (manifest.TimeoutSeconds <= 0)
                {
                    manifest.TimeoutSeconds = 30;
                }

                discovered[manifest.Name] = manifest;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to load script manifest at {Path}", manifestPath);
            }
        }

        _scripts.Clear();
        foreach (var script in discovered)
        {
            _scripts.TryAdd(script.Key, script.Value);
        }

        Logger.LogInformation("Scripting tool scanned {Count} script tools from {Workspace}", _scripts.Count, _workspaceDirectory);
    }

    private async Task<ScriptProcessResult> RunScriptProcessAsync(
        string interpreter,
        string entrypoint,
        string workingDirectory,
        string argumentsJson,
        int timeoutSeconds,
        CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = interpreter,
            Arguments = entrypoint,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stdoutTruncated = false;
        var stderrTruncated = false;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                AppendWithLimit(stdoutBuilder, e.Data + Environment.NewLine, _maxOutputLength, ref stdoutTruncated);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                AppendWithLimit(stderrBuilder, e.Data + Environment.NewLine, _maxOutputLength, ref stderrTruncated);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.StandardInput.WriteAsync(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        await process.StandardInput.WriteAsync(Environment.NewLine);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort process cleanup.
            }
        }

        if (stdoutTruncated)
        {
            stdoutBuilder.AppendLine("... (truncated)");
        }

        if (stderrTruncated)
        {
            stderrBuilder.AppendLine("... (truncated)");
        }

        return new ScriptProcessResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            TimedOut = timedOut,
            Stdout = stdoutBuilder.ToString(),
            Stderr = stderrBuilder.ToString(),
            StdoutTruncated = stdoutTruncated,
            StderrTruncated = stderrTruncated
        };
    }

    private static string SerializeParametersSchema(ScriptParameters? parameters)
    {
        if (parameters == null)
        {
            return """{"type":"object","properties":{},"required":[]}""";
        }

        return JsonSerializer.Serialize(parameters, JsonOptions);
    }

    private string GetScriptDirectory(string name) => Path.Combine(_workspaceDirectory, name);

    private static string GetManifestPath(string scriptDirectory) => Path.Combine(scriptDirectory, "manifest.json");

    private static string ResolveEntrypointPath(string scriptDirectory, string entrypoint)
    {
        if (string.IsNullOrWhiteSpace(entrypoint))
        {
            throw new InvalidOperationException("Entrypoint cannot be empty.");
        }

        var combined = Path.GetFullPath(Path.Combine(scriptDirectory, entrypoint));
        var fullScriptDirectory = Path.GetFullPath(scriptDirectory);
        if (!combined.StartsWith(fullScriptDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !string.Equals(combined, fullScriptDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Entrypoint must resolve within the script directory.");
        }

        return combined;
    }

    private static void AppendWithLimit(StringBuilder builder, string text, int maxLength, ref bool truncated)
    {
        if (truncated || maxLength <= 0)
        {
            truncated = true;
            return;
        }

        var remaining = maxLength - builder.Length;
        if (remaining <= 0)
        {
            truncated = true;
            return;
        }

        if (text.Length <= remaining)
        {
            builder.Append(text);
            return;
        }

        builder.Append(text[..remaining]);
        truncated = true;
    }

    private sealed class CreateScriptArgs
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Interpreter { get; set; }
        public string? Entrypoint { get; set; }
        public ScriptParameters? Parameters { get; set; }

        [JsonPropertyName("timeout_seconds")]
        public int? TimeoutSeconds { get; set; }

        public string? Content { get; set; }
    }

    private sealed class NamedScriptArgs
    {
        public string? Name { get; set; }
    }

    private sealed class EditScriptArgs
    {
        public string? Name { get; set; }
        public string? Content { get; set; }
    }

    private sealed class ScriptProcessResult
    {
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public string Stdout { get; set; } = string.Empty;
        public string Stderr { get; set; } = string.Empty;
        public bool StdoutTruncated { get; set; }
        public bool StderrTruncated { get; set; }
    }
}
