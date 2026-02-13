using Botty.Core.Interfaces;
using Botty.Scheduler.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Botty.Scheduler;

/// <summary>
/// Extension methods for registering scheduler services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds scheduler services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableBackgroundService">Whether to enable the scheduler background service. Default: true.</param>
    public static IServiceCollection AddSchedulerServices(
        this IServiceCollection services,
        bool enableBackgroundService = true)
    {
        // Cron parser
        services.AddSingleton<ICronParser, CronParser>();
        
        // Scheduler service
        services.AddScoped<ISchedulerService, SchedulerService>();
        
        // Background service
        if (enableBackgroundService)
        {
            services.AddHostedService<SchedulerBackgroundService>();
        }

        return services;
    }
}
