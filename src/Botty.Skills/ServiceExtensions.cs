using Botty.Core.Interfaces;
using Botty.Skills.BraveSearch;
using Botty.Skills.Calendar;
using Botty.Skills.Gmail;
using Botty.Skills.InteractiveBrokers;
using Botty.Skills.Registry;
using Botty.Skills.Repositories;
using Botty.Skills.Scheduler;
using Botty.Skills.Services;
using Botty.Skills.Browser;
using Botty.Skills.Channels;
using Botty.Skills.Shell;
using Botty.Skills.Scripting;
using Botty.Skills.UrlFetch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Botty.Skills;

/// <summary>
/// Extension methods for registering skill services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds skill framework services to the service collection.
    /// </summary>
    public static IServiceCollection AddSkillsFramework(
        this IServiceCollection services,
        string connectionString)
    {
        // Register repository
        services.AddSingleton<ISkillConfigRepository>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SkillConfigRepository>>();
            return new SkillConfigRepository(connectionString, logger);
        });
        services.AddSingleton<ILinkedAccountService>(sp =>
        {
            var secretStore = sp.GetRequiredService<ISecretStore>();
            var logger = sp.GetRequiredService<ILogger<LinkedAccountService>>();
            return new LinkedAccountService(connectionString, secretStore, logger);
        });

        // Register config service
        services.AddSingleton<SkillConfigService>();
        services.AddSingleton<ISkillConfigService>(sp => sp.GetRequiredService<SkillConfigService>());
        services.AddHttpClient();

        // Register skills
        services.AddSingleton<GmailSkill>();
        services.AddSingleton<GoogleCalendarSkill>();
        services.AddSingleton<ShellSkill>();
        services.AddSingleton<SchedulerSkill>();
        services.AddSingleton<BraveSearchSkill>();
        services.AddSingleton<UrlFetchSkill>();
        services.AddSingleton<ScriptSkill>();
        services.AddSingleton<ChannelMessagingSkill>();
        services.AddSingleton<IbClient>();
        services.AddSingleton<InteractiveBrokersSkill>();
        services.AddSingleton<BrowserSession>();
        services.AddSingleton<BrowserSkill>();

        // Register skill registry
        services.AddSingleton<SkillRegistry>();
        services.AddSingleton<ISkillRegistry>(sp => sp.GetRequiredService<SkillRegistry>());

        return services;
    }

    /// <summary>
    /// Initializes the skill framework by registering skills and their configurations.
    /// Call this after the service provider is built.
    /// </summary>
    public static async Task InitializeSkillsAsync(this IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<SkillRegistry>>();
        
        // Ensure database table exists
        var repository = serviceProvider.GetRequiredService<ISkillConfigRepository>();
        if (repository is SkillConfigRepository pgRepo)
        {
            await pgRepo.EnsureTableExistsAsync(ct);
        }
        var linkedAccountService = serviceProvider.GetRequiredService<ILinkedAccountService>();
        await linkedAccountService.EnsureStoreAsync(ct);

        // Get services
        var configService = serviceProvider.GetRequiredService<SkillConfigService>();
        var registry = serviceProvider.GetRequiredService<SkillRegistry>();

        // Get skill instances
        var gmailSkill = serviceProvider.GetRequiredService<GmailSkill>();
        var calendarSkill = serviceProvider.GetRequiredService<GoogleCalendarSkill>();
        var shellSkill = serviceProvider.GetRequiredService<ShellSkill>();
        var schedulerSkill = serviceProvider.GetRequiredService<SchedulerSkill>();
        var braveSearchSkill = serviceProvider.GetRequiredService<BraveSearchSkill>();
        var urlFetchSkill = serviceProvider.GetRequiredService<UrlFetchSkill>();
        var scriptSkill = serviceProvider.GetRequiredService<ScriptSkill>();
        var channelMessagingSkill = serviceProvider.GetRequiredService<ChannelMessagingSkill>();
        var interactiveBrokersSkill = serviceProvider.GetRequiredService<InteractiveBrokersSkill>();
        var browserSkill = serviceProvider.GetRequiredService<BrowserSkill>();

        // Register configuration schemas
        configService.RegisterSchema(gmailSkill.ConfigSchema);
        configService.RegisterSchema(calendarSkill.ConfigSchema);
        configService.RegisterSchema(shellSkill.ConfigSchema);
        configService.RegisterSchema(schedulerSkill.ConfigSchema);
        configService.RegisterSchema(braveSearchSkill.ConfigSchema);
        configService.RegisterSchema(urlFetchSkill.ConfigSchema);
        configService.RegisterSchema(scriptSkill.ConfigSchema);
        configService.RegisterSchema(channelMessagingSkill.ConfigSchema);
        configService.RegisterSchema(interactiveBrokersSkill.ConfigSchema);
        configService.RegisterSchema(browserSkill.ConfigSchema);

        // Register skills with the registry
        registry.Register(gmailSkill);
        registry.Register(calendarSkill);
        registry.Register(shellSkill);
        registry.Register(schedulerSkill);
        registry.Register(braveSearchSkill);
        registry.Register(urlFetchSkill);
        registry.Register(scriptSkill);
        registry.Register(channelMessagingSkill);
        registry.Register(interactiveBrokersSkill);
        registry.Register(browserSkill);

        // Initialize all skills
        await registry.InitializeAllAsync(ct);

        logger.LogInformation("Skills framework initialized with {Count} skills", registry.GetAll().Count());
    }
}
