using Botty.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botty.Memory.Services;

/// <summary>
/// Background service that periodically cleans up expired memories.
/// </summary>
public class MemoryCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MemoryCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

    public MemoryCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<MemoryCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Memory cleanup service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredMemoriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during memory cleanup");
            }

            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("Memory cleanup service stopping");
    }

    private async Task CleanupExpiredMemoriesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemoryRepository>();

        var expiredMemories = await repository.GetExpiredMemoriesAsync(ct);
        var count = 0;

        foreach (var memory in expiredMemories)
        {
            await repository.DeleteAsync(memory.Id, ct);
            count++;
        }

        if (count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired memories", count);
        }
    }
}
