using Botty.Core.Interfaces;
using Botty.Tools.BraveSearch;
using Botty.Tools.Calendar;
using Botty.Tools.Gmail;
using Botty.Tools.InteractiveBrokers;
using Botty.Tools.Registry;
using Botty.Tools.Repositories;
using Botty.Tools.Scheduler;
using Botty.Tools.Services;
using Botty.Tools.Browser;
using Botty.Tools.Channels;
using Botty.Tools.Shell;
using Botty.Tools.Scripting;
using DateTimeToolClass = Botty.Tools.DateTimeTool.DateTimeTool;
using Botty.Tools.UrlFetch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Botty.Tools;

/// <summary>
/// Extension methods for registering tool services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds tool framework services to the service collection.
    /// </summary>
    public static IServiceCollection AddToolsFramework(
        this IServiceCollection services,
        string connectionString)
    {
        // Register repository
        services.AddSingleton<IToolConfigRepository>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ToolConfigRepository>>();
            return new ToolConfigRepository(connectionString, logger);
        });
        services.AddSingleton<ILinkedAccountService>(sp =>
        {
            var secretStore = sp.GetRequiredService<ISecretStore>();
            var logger = sp.GetRequiredService<ILogger<LinkedAccountService>>();
            return new LinkedAccountService(connectionString, secretStore, logger);
        });

        // Register config service
        services.AddSingleton<ToolConfigService>();
        services.AddSingleton<IToolConfigService>(sp => sp.GetRequiredService<ToolConfigService>());
        services.AddHttpClient();

        // Register tools
        services.AddSingleton<GmailTool>();
        services.AddSingleton<GoogleCalendarTool>();
        services.AddSingleton<ShellTool>();
        services.AddSingleton<SchedulerTool>();
        services.AddSingleton<BraveSearchTool>();
        services.AddSingleton<UrlFetchTool>();
        services.AddSingleton<ScriptTool>();
        services.AddSingleton<ChannelMessagingTool>();
        services.AddSingleton<IbClient>();
        services.AddSingleton<InteractiveBrokersTool>();
        services.AddSingleton<BrowserSession>();
        services.AddSingleton<BrowserTool>();
        services.AddSingleton<DateTimeToolClass>();

        // Register tool registry
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistry>());

        return services;
    }

    /// <summary>
    /// Initializes the tool framework by registering tools and their configurations.
    /// Call this after the service provider is built.
    /// </summary>
    public static async Task InitializeToolsAsync(this IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<ToolRegistry>>();
        
        // Ensure database table exists
        var repository = serviceProvider.GetRequiredService<IToolConfigRepository>();
        if (repository is ToolConfigRepository pgRepo)
        {
            await pgRepo.EnsureTableExistsAsync(ct);
        }
        var linkedAccountService = serviceProvider.GetRequiredService<ILinkedAccountService>();
        await linkedAccountService.EnsureStoreAsync(ct);

        // Get services
        var configService = serviceProvider.GetRequiredService<ToolConfigService>();
        var registry = serviceProvider.GetRequiredService<ToolRegistry>();

        // Get tool instances
        var gmailTool = serviceProvider.GetRequiredService<GmailTool>();
        var calendarTool = serviceProvider.GetRequiredService<GoogleCalendarTool>();
        var shellTool = serviceProvider.GetRequiredService<ShellTool>();
        var schedulerTool = serviceProvider.GetRequiredService<SchedulerTool>();
        var braveSearchTool = serviceProvider.GetRequiredService<BraveSearchTool>();
        var urlFetchTool = serviceProvider.GetRequiredService<UrlFetchTool>();
        var scriptTool = serviceProvider.GetRequiredService<ScriptTool>();
        var channelMessagingTool = serviceProvider.GetRequiredService<ChannelMessagingTool>();
        var interactiveBrokersTool = serviceProvider.GetRequiredService<InteractiveBrokersTool>();
        var browserTool = serviceProvider.GetRequiredService<BrowserTool>();
        var dateTimeTool = serviceProvider.GetRequiredService<DateTimeToolClass>();

        // Register configuration schemas
        configService.RegisterSchema(gmailTool.ConfigSchema);
        configService.RegisterSchema(calendarTool.ConfigSchema);
        configService.RegisterSchema(shellTool.ConfigSchema);
        configService.RegisterSchema(schedulerTool.ConfigSchema);
        configService.RegisterSchema(braveSearchTool.ConfigSchema);
        configService.RegisterSchema(urlFetchTool.ConfigSchema);
        configService.RegisterSchema(scriptTool.ConfigSchema);
        configService.RegisterSchema(channelMessagingTool.ConfigSchema);
        configService.RegisterSchema(interactiveBrokersTool.ConfigSchema);
        configService.RegisterSchema(browserTool.ConfigSchema);
        configService.RegisterSchema(dateTimeTool.ConfigSchema);

        // Register tools with the registry
        registry.Register(gmailTool);
        registry.Register(calendarTool);
        registry.Register(shellTool);
        registry.Register(schedulerTool);
        registry.Register(braveSearchTool);
        registry.Register(urlFetchTool);
        registry.Register(scriptTool);
        registry.Register(channelMessagingTool);
        registry.Register(interactiveBrokersTool);
        registry.Register(browserTool);
        registry.Register(dateTimeTool);

        // Initialize all tools
        await registry.InitializeAllAsync(ct);

        logger.LogInformation("Tools framework initialized with {Count} tools", registry.GetAll().Count());
    }
}
