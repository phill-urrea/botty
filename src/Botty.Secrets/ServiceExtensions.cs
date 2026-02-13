using Botty.Core.Interfaces;
using Botty.Secrets.Implementations;
using Google.Cloud.SecretManager.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Botty.Secrets;

/// <summary>
/// Extension methods for registering secret store services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds the local secret store for development.
    /// </summary>
    public static IServiceCollection AddLocalSecretStore(this IServiceCollection services)
    {
        services.AddSingleton<ISecretStore, LocalSecretStore>();
        return services;
    }

    /// <summary>
    /// Adds the GCP Secret Manager store for production.
    /// </summary>
    public static IServiceCollection AddGcpSecretStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GcpSecretStoreOptions>(options =>
        {
            options.ProjectId = configuration["GCP:ProjectId"] 
                ?? throw new InvalidOperationException("GCP:ProjectId configuration is required");
        });

        services.AddSingleton(SecretManagerServiceClient.Create());
        services.AddSingleton<ISecretStore, GcpSecretStore>();
        
        return services;
    }

    /// <summary>
    /// Adds the appropriate secret store based on environment.
    /// </summary>
    public static IServiceCollection AddSecretStore(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        if (isDevelopment)
        {
            return services.AddLocalSecretStore();
        }
        else
        {
            return services.AddGcpSecretStore(configuration);
        }
    }
}
