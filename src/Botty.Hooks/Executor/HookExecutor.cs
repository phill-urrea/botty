using Botty.Hooks.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Hooks.Executor;

/// <summary>
/// Executes hooks with timeout and logging.
/// </summary>
public class HookExecutor : IHookExecutor
{
    private readonly ILogger<HookExecutor> _logger;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

    public HookExecutor(ILogger<HookExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> ExecuteAsync(IHook hook, HookContext context, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_defaultTimeout);
            var result = await hook.ExecuteAsync(context, cts.Token);
            sw.Stop();
            return new HookResult
            {
                Success = result.Success,
                Output = result.Output,
                Error = result.Error,
                Duration = sw.Elapsed,
                Metadata = result.Metadata
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("Hook {HookId} timed out", hook.Id);
            return new HookResult { Success = false, Error = "Timeout", Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Hook {HookId} threw", hook.Id);
            return new HookResult { Success = false, Error = ex.Message, Duration = sw.Elapsed };
        }
    }
}
