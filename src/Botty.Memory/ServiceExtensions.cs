using Botty.Core.Interfaces;
using Botty.Memory.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Botty.Memory;

/// <summary>
/// Extension methods for registering memory services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds memory system services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableCleanupService">Whether to enable the background cleanup service. Default: true.</param>
    public static IServiceCollection AddMemorySystem(
        this IServiceCollection services,
        bool enableCleanupService = true)
    {
        // Extraction and processing
        services.AddScoped<IMemoryExtractor, MemoryExtractor>();
        services.AddScoped<IMemoryScorer, MemoryScorer>();
        services.AddScoped<IMemoryDeduplicator, MemoryDeduplicator>();
        
        // Hybrid search
        services.Configure<HybridSearchOptions>(options => { });
        services.AddScoped<IHybridSearchService, HybridSearchService>();

        // Ingestion and retrieval
        services.AddScoped<IMemoryIngestionService, MemoryIngestionService>();
        services.AddScoped<IMemoryRetrievalService, MemoryRetrievalService>();

        // Trust layer
        services.AddScoped<IMemoryTrustService, MemoryTrustService>();

        // Background cleanup service
        if (enableCleanupService)
        {
            services.AddHostedService<MemoryCleanupService>();
        }

        // Configure options
        services.Configure<MemoryIngestionOptions>(options =>
        {
            options.MinimumScoreThreshold = 0.4m;
            options.MaxMemoriesPerConversation = 5;
        });
        
        return services;
    }
}
