using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Base;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Botty.Tools.Shell;

/// <summary>
/// Tool for executing shell commands with safety controls.
/// </summary>
public partial class ShellTool : BaseTool
{
    private string _workingDirectory = Environment.CurrentDirectory;
    private int _maxOutputLength = 50000;
    private int _defaultTimeoutSeconds = 60;
    private HashSet<string> _blockedCommands = [];
    private HashSet<string> _blockedPatterns = [];
    private bool _allowSudo;

    public ShellTool(ILogger<ShellTool> logger) : base(logger)
    {
    }

    public override string Id => "shell";
    public override string Name => "Shell";
    public override string Description => "Execute shell commands, run scripts, and manage system operations with safety controls.";

    public override ToolConfigSchema ConfigSchema => new()
    {
        ToolId = Id,
        Fields =
        [
            new ConfigField
            {
                Key = "working_directory",
                Label = "Working Directory",
                Description = "Default working directory for commands",
                Type = ConfigFieldType.String,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "/app"
            },
            new ConfigField
            {
                Key = "max_output_length",
                Label = "Max Output Length",
                Description = "Maximum characters to return from command output",
                Type = ConfigFieldType.Number,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "50000"
            },
            new ConfigField
            {
                Key = "default_timeout_seconds",
                Label = "Default Timeout",
                Description = "Default timeout for commands in seconds",
                Type = ConfigFieldType.Number,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "60"
            },
            new ConfigField
            {
                Key = "blocked_commands",
                Label = "Blocked Commands",
                Description = "Comma-separated list of blocked commands (e.g., 'rm -rf,shutdown')",
                Type = ConfigFieldType.String,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "shutdown,reboot,init,halt,poweroff"
            },
            new ConfigField
            {
                Key = "blocked_patterns",
                Label = "Blocked Patterns",
                Description = "Comma-separated list of regex patterns to block",
                Type = ConfigFieldType.String,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = @"rm\s+-rf\s+/,mkfs\.,dd\s+if=.*of=/dev/"
            },
            new ConfigField
            {
                Key = "allow_sudo",
                Label = "Allow Sudo",
                Description = "Whether to allow sudo commands",
                Type = ConfigFieldType.Boolean,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "false"
            },
            new ConfigField
            {
                Key = "environment_variables",
                Label = "Environment Variables",
                Description = "JSON object of additional environment variables",
                Type = ConfigFieldType.Json,
                IsSensitive = true,
                IsRequired = false
            }
        ]
    };

    protected override Task OnInitializeAsync(CancellationToken ct)
    {
        var workDir = GetConfig("working_directory");
        if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
        {
            _workingDirectory = workDir;
        }

        if (int.TryParse(GetConfig("max_output_length"), out var maxOutput))
        {
            _maxOutputLength = maxOutput;
        }

        if (int.TryParse(GetConfig("default_timeout_seconds"), out var timeout))
        {
            _defaultTimeoutSeconds = timeout;
        }

        var blocked = GetConfig("blocked_commands");
        if (!string.IsNullOrEmpty(blocked))
        {
            _blockedCommands = blocked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var patterns = GetConfig("blocked_patterns");
        if (!string.IsNullOrEmpty(patterns))
        {
            _blockedPatterns = patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet();
        }

        _allowSudo = GetConfig("allow_sudo")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        Logger.LogInformation("Shell tool initialized. Working directory: {WorkDir}, Sudo allowed: {AllowSudo}", 
            _workingDirectory, _allowSudo);

        return Task.CompletedTask;
    }

    public override IEnumerable<LlmTool> GetTools()
    {
        return new[]
        {
            new LlmTool
            {
                Name = "shell_execute",
                Description = "Execute a shell command (requires approval for destructive commands)",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "command": { "type": "string", "description": "The command to execute" },
                        "workingDirectory": { "type": "string", "description": "Working directory for the command" },
                        "timeoutSeconds": { "type": "integer", "description": "Timeout in seconds" },
                        "captureOutput": { "type": "boolean", "description": "Whether to capture and return output (default: true)" }
                    },
                    "required": ["command"]
                }
                """
            },
            new LlmTool
            {
                Name = "shell_script",
                Description = "Execute a multi-line shell script",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "script": { "type": "string", "description": "The script content to execute" },
                        "interpreter": { "type": "string", "description": "Script interpreter (default: /bin/bash)" },
                        "workingDirectory": { "type": "string", "description": "Working directory for the script" },
                        "timeoutSeconds": { "type": "integer", "description": "Timeout in seconds" }
                    },
                    "required": ["script"]
                }
                """
            },
            new LlmTool
            {
                Name = "shell_read_file",
                Description = "Read the contents of a file",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "path": { "type": "string", "description": "Path to the file to read" },
                        "encoding": { "type": "string", "description": "File encoding (default: utf-8)" }
                    },
                    "required": ["path"]
                }
                """
            },
            new LlmTool
            {
                Name = "shell_write_file",
                Description = "Write content to a file (requires approval)",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "path": { "type": "string", "description": "Path to the file to write" },
                        "content": { "type": "string", "description": "Content to write" },
                        "append": { "type": "boolean", "description": "Whether to append instead of overwrite" },
                        "encoding": { "type": "string", "description": "File encoding (default: utf-8)" }
                    },
                    "required": ["path", "content"]
                }
                """
            },
            new LlmTool
            {
                Name = "shell_list_directory",
                Description = "List contents of a directory",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "path": { "type": "string", "description": "Path to the directory" },
                        "recursive": { "type": "boolean", "description": "Whether to list recursively" },
                        "includeHidden": { "type": "boolean", "description": "Whether to include hidden files" }
                    },
                    "required": ["path"]
                }
                """
            },
            new LlmTool
            {
                Name = "shell_get_environment",
                Description = "Get environment variables",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "variable": { "type": "string", "description": "Specific variable to get (optional, returns all if not specified)" }
                    },
                    "required": []
                }
                """
            },
            new LlmTool
            {
                Name = "shell_which",
                Description = "Find the path of an executable",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "command": { "type": "string", "description": "Command to find" }
                    },
                    "required": ["command"]
                }
                """
            }
        };
    }

    protected override async Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct)
    {
        return context.ToolName switch
        {
            "shell_execute" => await ExecuteCommandAsync(context.Arguments, ct),
            "shell_script" => await ExecuteScriptAsync(context.Arguments, ct),
            "shell_read_file" => await ReadFileAsync(context.Arguments, ct),
            "shell_write_file" => await WriteFileAsync(context.Arguments, ct),
            "shell_list_directory" => await ListDirectoryAsync(context.Arguments, ct),
            "shell_get_environment" => GetEnvironment(context.Arguments),
            "shell_which" => await WhichAsync(context.Arguments, ct),
            _ => ToolResult.Fail($"Unknown tool: {context.ToolName}")
        };
    }

    private async Task<ToolResult> ExecuteCommandAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<ExecuteArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.Command))
            return ToolResult.Fail("Invalid arguments: command required");

        // Safety check
        var safetyResult = CheckCommandSafety(args.Command);
        if (!safetyResult.IsSafe)
        {
            return ToolResult.Fail($"Command blocked: {safetyResult.Reason}");
        }

        var workDir = args.WorkingDirectory ?? _workingDirectory;
        var timeout = args.TimeoutSeconds ?? _defaultTimeoutSeconds;

        try
        {
            var result = await RunProcessAsync(
                "/bin/bash", 
                $"-c \"{args.Command.Replace("\"", "\\\"")}\"", 
                workDir, 
                timeout, 
                args.CaptureOutput ?? true,
                ct);

            return ToolResult.Ok(ToJson(result));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Command execution failed: {Command}", args.Command);
            return ToolResult.Fail($"Command failed: {ex.Message}");
        }
    }

    private async Task<ToolResult> ExecuteScriptAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<ScriptArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.Script))
            return ToolResult.Fail("Invalid arguments: script required");

        // Check each line of the script for safety
        var lines = args.Script.Split('\n');
        foreach (var line in lines)
        {
            var safetyResult = CheckCommandSafety(line);
            if (!safetyResult.IsSafe)
            {
                return ToolResult.Fail($"Script blocked at line: {safetyResult.Reason}");
            }
        }

        var interpreter = args.Interpreter ?? "/bin/bash";
        var workDir = args.WorkingDirectory ?? _workingDirectory;
        var timeout = args.TimeoutSeconds ?? _defaultTimeoutSeconds;

        // Write script to temp file
        var scriptPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(scriptPath, args.Script, ct);
            
            var result = await RunProcessAsync(interpreter, scriptPath, workDir, timeout, true, ct);
            return ToolResult.Ok(ToJson(result));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private async Task<ToolResult> ReadFileAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<ReadFileArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.Path))
            return ToolResult.Fail("Invalid arguments: path required");

        try
        {
            var encoding = GetEncoding(args.Encoding);
            var content = await File.ReadAllTextAsync(args.Path, encoding, ct);

            if (content.Length > _maxOutputLength)
            {
                content = content[.._maxOutputLength] + "\n... (truncated)";
            }

            return ToolResult.Ok(ToJson(new
            {
                path = args.Path,
                content,
                length = content.Length,
                truncated = content.Length > _maxOutputLength
            }));
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to read file: {ex.Message}");
        }
    }

    private async Task<ToolResult> WriteFileAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<WriteFileArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.Path))
            return ToolResult.Fail("Invalid arguments: path required");

        try
        {
            var encoding = GetEncoding(args.Encoding);
            var directory = Path.GetDirectoryName(args.Path);
            
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (args.Append)
            {
                await File.AppendAllTextAsync(args.Path, args.Content, encoding, ct);
            }
            else
            {
                await File.WriteAllTextAsync(args.Path, args.Content, encoding, ct);
            }

            return ToolResult.Ok(ToJson(new
            {
                success = true,
                path = args.Path,
                bytesWritten = encoding.GetByteCount(args.Content ?? ""),
                append = args.Append
            }));
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to write file: {ex.Message}");
        }
    }

    private Task<ToolResult> ListDirectoryAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<ListDirArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.Path))
            return Task.FromResult(ToolResult.Fail("Invalid arguments: path required"));

        try
        {
            var searchOption = args.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            
            var entries = new List<object>();
            
            foreach (var dir in Directory.GetDirectories(args.Path, "*", searchOption))
            {
                var dirInfo = new DirectoryInfo(dir);
                if (!args.IncludeHidden && dirInfo.Name.StartsWith('.'))
                    continue;

                entries.Add(new
                {
                    name = dirInfo.Name,
                    path = dir,
                    type = "directory",
                    modified = dirInfo.LastWriteTimeUtc
                });
            }

            foreach (var file in Directory.GetFiles(args.Path, "*", searchOption))
            {
                var fileInfo = new FileInfo(file);
                if (!args.IncludeHidden && fileInfo.Name.StartsWith('.'))
                    continue;

                entries.Add(new
                {
                    name = fileInfo.Name,
                    path = file,
                    type = "file",
                    size = fileInfo.Length,
                    modified = fileInfo.LastWriteTimeUtc
                });
            }

            return Task.FromResult(ToolResult.Ok(ToJson(new
            {
                path = args.Path,
                entries,
                count = entries.Count
            })));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Failed to list directory: {ex.Message}"));
        }
    }

    private ToolResult GetEnvironment(string arguments)
    {
        var args = ParseArguments<EnvArgs>(arguments);

        if (!string.IsNullOrEmpty(args?.Variable))
        {
            var value = Environment.GetEnvironmentVariable(args.Variable);
            return ToolResult.Ok(ToJson(new
            {
                variable = args.Variable,
                value,
                found = value != null
            }));
        }

        // Return all (non-sensitive) environment variables
        var envVars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(e => !IsSensitiveEnvVar(e.Key.ToString()!))
            .ToDictionary(e => e.Key.ToString()!, e => e.Value?.ToString());

        return ToolResult.Ok(ToJson(new { variables = envVars, count = envVars.Count }));
    }

    private async Task<ToolResult> WhichAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<WhichArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.Command))
            return ToolResult.Fail("Invalid arguments: command required");

        try
        {
            var result = await RunProcessAsync("/usr/bin/which", args.Command, _workingDirectory, 10, true, ct);
            
            return ToolResult.Ok(ToJson(new
            {
                command = args.Command,
                path = result.Stdout?.Trim(),
                found = result.ExitCode == 0
            }));
        }
        catch
        {
            return ToolResult.Ok(ToJson(new
            {
                command = args.Command,
                path = (string?)null,
                found = false
            }));
        }
    }

    private async Task<ProcessResult> RunProcessAsync(
        string fileName, 
        string arguments, 
        string workingDirectory,
        int timeoutSeconds,
        bool captureOutput,
        CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        if (captureOutput)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        }

        process.Start();
        
        if (captureOutput)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Command timed out after {timeoutSeconds} seconds");
        }

        var stdoutStr = stdout.ToString();
        var stderrStr = stderr.ToString();

        if (stdoutStr.Length > _maxOutputLength)
            stdoutStr = stdoutStr[.._maxOutputLength] + "\n... (truncated)";
        if (stderrStr.Length > _maxOutputLength)
            stderrStr = stderrStr[.._maxOutputLength] + "\n... (truncated)";

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            Stdout = stdoutStr,
            Stderr = stderrStr,
            Success = process.ExitCode == 0
        };
    }

    private (bool IsSafe, string? Reason) CheckCommandSafety(string command)
    {
        var trimmed = command.Trim();
        
        if (string.IsNullOrEmpty(trimmed))
            return (true, null);

        // Check for sudo if not allowed
        if (!_allowSudo && (trimmed.StartsWith("sudo ") || trimmed.Contains(" sudo ")))
        {
            return (false, "sudo commands are not allowed");
        }

        // Check blocked commands
        var firstWord = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstWord != null && _blockedCommands.Contains(firstWord))
        {
            return (false, $"Command '{firstWord}' is blocked");
        }

        // Check blocked patterns
        foreach (var pattern in _blockedPatterns)
        {
            try
            {
                if (Regex.IsMatch(trimmed, pattern, RegexOptions.IgnoreCase))
                {
                    return (false, $"Command matches blocked pattern");
                }
            }
            catch { /* ignore invalid regex */ }
        }

        return (true, null);
    }

    private static bool IsSensitiveEnvVar(string name)
    {
        var sensitive = new[] { "PASSWORD", "SECRET", "TOKEN", "KEY", "CREDENTIAL", "AUTH" };
        return sensitive.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private static Encoding GetEncoding(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            "ascii" => Encoding.ASCII,
            "utf-16" or "unicode" => Encoding.Unicode,
            "utf-32" => Encoding.UTF32,
            _ => Encoding.UTF8
        };
    }

    // Argument classes
    private class ExecuteArgs
    {
        public string? Command { get; set; }
        public string? WorkingDirectory { get; set; }
        public int? TimeoutSeconds { get; set; }
        public bool? CaptureOutput { get; set; }
    }

    private class ScriptArgs
    {
        public string? Script { get; set; }
        public string? Interpreter { get; set; }
        public string? WorkingDirectory { get; set; }
        public int? TimeoutSeconds { get; set; }
    }

    private class ReadFileArgs
    {
        public string? Path { get; set; }
        public string? Encoding { get; set; }
    }

    private class WriteFileArgs
    {
        public string? Path { get; set; }
        public string? Content { get; set; }
        public bool Append { get; set; }
        public string? Encoding { get; set; }
    }

    private class ListDirArgs
    {
        public string? Path { get; set; }
        public bool Recursive { get; set; }
        public bool IncludeHidden { get; set; }
    }

    private class EnvArgs
    {
        public string? Variable { get; set; }
    }

    private class WhichArgs
    {
        public string? Command { get; set; }
    }

    private class ProcessResult
    {
        public int ExitCode { get; set; }
        public string? Stdout { get; set; }
        public string? Stderr { get; set; }
        public bool Success { get; set; }
    }
}
