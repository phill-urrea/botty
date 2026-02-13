using Microsoft.Extensions.DependencyInjection;

namespace Botty.Messaging;

/// <summary>
/// Extension methods for registering messaging services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds messaging services to the service collection.
    /// </summary>
    public static IServiceCollection AddMessagingServices(this IServiceCollection services)
    {
        // TODO: Register messaging services
        // services.AddScoped<IWhatsAppClient, WhatsAppClient>();
        // services.AddScoped<IMessageProcessor, MessageProcessor>();
        
        return services;
    }
}
