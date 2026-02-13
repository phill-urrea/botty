using Botty.Core.Interfaces;
using Botty.Infrastructure.Data;
using Botty.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Botty.Infrastructure;

/// <summary>
/// Extension methods for registering infrastructure services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds the database context and repositories to the service collection.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add DbContext
        services.AddDbContext<BottyDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsqlOptions =>
                {
                    npgsqlOptions.UseVector();
                });
        });

        // Register repositories
        services.AddScoped<IMemoryRepository, MemoryRepository>();
        services.AddScoped<MemoryRepository>(); // Also register concrete type for services that need extended methods
        
        services.AddScoped<IKanbanRepository, KanbanRepository>();
        services.AddScoped<KanbanRepository>(); // Also register concrete type for extended methods
        
        services.AddScoped<ISchedulerRepository, SchedulerRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();

        // Embedding cache
        services.AddScoped<EmbeddingCacheRepository>();

        // Pairing & allow list
        services.AddScoped<PairingRepository>();

        return services;
    }
}
