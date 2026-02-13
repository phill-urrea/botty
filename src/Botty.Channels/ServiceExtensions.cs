using Botty.Channels.Discord;
using Botty.Channels.Registry;
using Botty.Channels.Services;
using Botty.Channels.Slack;
using Botty.Channels.Telegram;
using Botty.Channels.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Botty.Channels;

public static class ServiceExtensions
{
    /// <summary>
    /// Add channel services to the DI container
    /// </summary>
    public static IServiceCollection AddChannels(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Get connection string
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string is required");

        // Register configuration repository
        services.AddSingleton<IChannelConfigRepository>(sp =>
            new ChannelConfigRepository(
                connectionString,
                sp.GetRequiredService<ILogger<ChannelConfigRepository>>()));

        // Register the channel registry
        services.AddSingleton<IChannelRegistry, ChannelRegistry>();
        services.AddSingleton<ChannelRegistry>(sp => (ChannelRegistry)sp.GetRequiredService<IChannelRegistry>());

        // Message chunking service
        services.Configure<MessageChunkingOptions>(options => { });
        services.AddSingleton<IMessageChunkingService, MessageChunkingService>();

        // Poll validation service
        services.AddSingleton<PollValidationService>();

        // Pairing service
        services.AddScoped<PairingService>();

        // WhatsApp security filter
        services.AddScoped<WhatsAppSecurityFilter>();

        // Configure channel options from configuration
        services.Configure<WhatsAppOptions>(configuration.GetSection("Channels:WhatsApp"));
        services.Configure<TelegramOptions>(configuration.GetSection("Channels:Telegram"));
        services.Configure<SlackOptions>(configuration.GetSection("Channels:Slack"));
        services.Configure<DiscordOptions>(configuration.GetSection("Channels:Discord"));

        // Configure WhatsApp security
        services.Configure<WhatsAppSecurityOptions>(configuration.GetSection("Channels:WhatsApp:Security"));

        // Register channel plugins
        services.AddSingleton<WhatsAppChannelPlugin>();
        services.AddSingleton<TelegramChannelPlugin>();
        services.AddSingleton<SlackChannelPlugin>();
        services.AddSingleton<DiscordChannelPlugin>();

        // Register channel initialization service
        services.AddHostedService<ChannelInitializationService>();

        return services;
    }
    
    /// <summary>
    /// Initialize channels - call after the DI container is built
    /// </summary>
    public static async Task InitializeChannelsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILogger<ChannelRegistry>>();
        
        // Ensure database table exists
        var configRepo = services.GetRequiredService<IChannelConfigRepository>();
        if (configRepo is ChannelConfigRepository pgRepo)
        {
            await pgRepo.EnsureTableExistsAsync(ct);
        }
        
        // Register all channel plugins with the registry
        var registry = services.GetRequiredService<IChannelRegistry>();
        
        var whatsApp = services.GetRequiredService<WhatsAppChannelPlugin>();
        var telegram = services.GetRequiredService<TelegramChannelPlugin>();
        var slack = services.GetRequiredService<SlackChannelPlugin>();
        var discord = services.GetRequiredService<DiscordChannelPlugin>();
        
        registry.Register(whatsApp);
        registry.Register(telegram);
        registry.Register(slack);
        registry.Register(discord);
        
        logger.LogInformation("Registered {Count} channel plugins", 4);
    }
}
