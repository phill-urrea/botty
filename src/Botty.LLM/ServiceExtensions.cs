using Botty.Core.Interfaces;
using Botty.LLM.Providers;
using Botty.LLM.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Botty.LLM;

/// <summary>
/// Extension methods for registering LLM services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds LLM services with placeholder providers (for development/testing).
    /// </summary>
    public static IServiceCollection AddLlmServices(this IServiceCollection services)
    {
        // Register placeholder providers
        services.AddSingleton<ILlmProvider, PlaceholderLlmProvider>();
        services.AddSingleton<IEmbeddingProvider, PlaceholderEmbeddingProvider>();

        // Register Soul service with default options
        services.Configure<SoulOptions>(_ => { });
        services.AddSingleton<ISoulService, SoulService>();

        // Register conversation orchestrator with default options
        services.Configure<ConversationOptions>(_ => { });
        services.AddScoped<IConversationOrchestrator, ConversationOrchestrator>();

        return services;
    }

    /// <summary>
    /// Adds LLM services with Claude provider and full configuration.
    /// </summary>
    public static IServiceCollection AddLlmServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool usePlaceholder = false)
    {
        // Configure Soul service
        services.Configure<SoulOptions>(options =>
        {
            var section = configuration.GetSection("Soul");
            options.FilePath = section["FilePath"] ?? "config/Soul.md";
        });
        services.AddSingleton<ISoulService, SoulService>();

        // Configure conversation orchestrator
        services.Configure<ConversationOptions>(options =>
        {
            var section = configuration.GetSection("Conversation");
            if (int.TryParse(section["MemoryExtractionMinTurns"], out var minTurns))
                options.MemoryExtractionMinTurns = minTurns;
            if (int.TryParse(section["MaxMemoryTokens"], out var maxTokens))
                options.MaxMemoryTokens = maxTokens;
            options.DefaultModel = section["DefaultModel"] ?? "claude-sonnet-4-20250514";
            if (int.TryParse(section["DefaultMaxTokens"], out var defaultMaxTokens))
                options.DefaultMaxTokens = defaultMaxTokens;
            if (float.TryParse(section["DefaultTemperature"], out var temp))
                options.DefaultTemperature = temp;
        });
        services.AddScoped<IConversationOrchestrator, ConversationOrchestrator>();

        if (usePlaceholder)
        {
            // Use placeholder providers for development/testing
            services.AddSingleton<ILlmProvider, PlaceholderLlmProvider>();
            services.AddSingleton<IEmbeddingProvider, PlaceholderEmbeddingProvider>();
        }
        else
        {
            // Configure Claude provider
            services.Configure<ClaudeOptions>(options =>
            {
                var section = configuration.GetSection("Claude");
                options.ApiKey = section["ApiKey"]
                    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                    ?? string.Empty;
                options.DefaultModel = section["DefaultModel"] ?? "claude-sonnet-4-20250514";
                if (int.TryParse(section["DefaultMaxTokens"], out var maxTokens))
                {
                    options.DefaultMaxTokens = maxTokens;
                }
            });
            services.AddSingleton<ILlmProvider, ClaudeProvider>();

            // Configure embedding provider via factory
            services.Configure<EmbeddingOptions>(configuration.GetSection("Embedding"));
            services.AddSingleton<EmbeddingProviderFactory>();
            services.AddSingleton<IEmbeddingProvider>(sp =>
                sp.GetRequiredService<EmbeddingProviderFactory>().Create());
        }

        return services;
    }

    /// <summary>
    /// Adds LLM services with a specific provider.
    /// </summary>
    public static IServiceCollection AddLlmServices<TProvider, TEmbedding>(this IServiceCollection services)
        where TProvider : class, ILlmProvider
        where TEmbedding : class, IEmbeddingProvider
    {
        services.AddSingleton<ILlmProvider, TProvider>();
        services.AddSingleton<IEmbeddingProvider, TEmbedding>();

        // Register Soul service with default options
        services.Configure<SoulOptions>(_ => { });
        services.AddSingleton<ISoulService, SoulService>();

        // Register conversation orchestrator with default options
        services.Configure<ConversationOptions>(_ => { });
        services.AddScoped<IConversationOrchestrator, ConversationOrchestrator>();

        return services;
    }
}
