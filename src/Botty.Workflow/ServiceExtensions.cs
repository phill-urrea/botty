using Botty.Core.Interfaces;
using Botty.Workflow.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Botty.Workflow;

/// <summary>
/// Extension methods for registering workflow services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds Kanban workflow services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableEventLoop">Whether to enable the assistant event loop. Default: true.</param>
    public static IServiceCollection AddWorkflowServices(
        this IServiceCollection services,
        bool enableEventLoop = true)
    {
        // Kanban and approval services
        services.AddScoped<IKanbanService, KanbanService>();
        services.AddScoped<IApprovalService, ApprovalService>();
        
        // Background event loop
        if (enableEventLoop)
        {
            services.AddHostedService<AssistantEventLoop>();
        }

        return services;
    }
}
