using System.Text.Json;
using Botty.Hooks;
using Botty.Hooks.Models;
using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Botty.Api.Services;

/// <summary>
/// Persists hook executions to the database.
/// </summary>
public class HookExecutionLogger : IHookExecutionLogger
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HookExecutionLogger> _logger;

    public HookExecutionLogger(IServiceScopeFactory scopeFactory, ILogger<HookExecutionLogger> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task LogAsync(Guid hookId, HookTrigger trigger, JsonElement? payload, HookResult result, CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BottyDbContext>();
            db.HookExecutions.Add(new HookExecutionEntity
            {
                Id = Guid.NewGuid(),
                HookId = hookId,
                Trigger = trigger.ToString(),
                PayloadJson = payload?.GetRawText(),
                Success = result.Success,
                Output = result.Output,
                Error = result.Error,
                DurationMs = (int)result.Duration.TotalMilliseconds,
                ExecutedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log hook execution for hook {HookId}", hookId);
        }
    }
}
