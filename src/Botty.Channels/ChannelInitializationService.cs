using Botty.Channels.Discord;
using Botty.Channels.Registry;
using Botty.Channels.Slack;
using Botty.Channels.Telegram;
using Botty.Channels.WhatsApp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botty.Channels;

/// <summary>
/// Background service that initializes channels on startup
/// </summary>
public class ChannelInitializationService : IHostedService
{
    private readonly IChannelRegistry _registry;
    private readonly IChannelConfigRepository _configRepository;
    private readonly ILogger<ChannelInitializationService> _logger;
    private readonly WhatsAppChannelPlugin _whatsApp;
    private readonly TelegramChannelPlugin _telegram;
    private readonly SlackChannelPlugin _slack;
    private readonly DiscordChannelPlugin _discord;
    
    public ChannelInitializationService(
        IChannelRegistry registry,
        IChannelConfigRepository configRepository,
        ILogger<ChannelInitializationService> logger,
        WhatsAppChannelPlugin whatsApp,
        TelegramChannelPlugin telegram,
        SlackChannelPlugin slack,
        DiscordChannelPlugin discord)
    {
        _registry = registry;
        _configRepository = configRepository;
        _logger = logger;
        _whatsApp = whatsApp;
        _telegram = telegram;
        _slack = slack;
        _discord = discord;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Channel initialization service starting");
        
        try
        {
            // Ensure database table exists
            if (_configRepository is ChannelConfigRepository pgRepo)
            {
                await pgRepo.EnsureTableExistsAsync(cancellationToken);
            }
            
            // Register all channel plugins
            _registry.Register(_whatsApp);
            _registry.Register(_telegram);
            _registry.Register(_slack);
            _registry.Register(_discord);
            
            _logger.LogInformation("Registered 4 channel plugins");
            
            // Initialize channels in the background so the app can start serving health checks
            _ = Task.Run(async () =>
            {
                try
                {
                    await _registry.InitializeAllAsync(cancellationToken);
                    _logger.LogInformation("Channel initialization complete");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during background channel initialization");
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during channel initialization");
        }
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Channel initialization service stopping");
        return Task.CompletedTask;
    }
}
