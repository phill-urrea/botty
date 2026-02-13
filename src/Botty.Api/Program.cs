using Botty.Api.Services;
using Botty.Channels;
using Botty.Hooks;
using Botty.Hooks.Actions;
using Botty.Hooks.Executor;
using Botty.Hooks.Registry;
using Botty.Infrastructure;
using Botty.LLM;
using Botty.Memory;
using Botty.Scheduler;
using Botty.Secrets;
using Botty.Skills;
using Botty.Workflow;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.Configure<OAuthOptions>(builder.Configuration.GetSection("OAuth:Providers"));

// CORS: allow admin UI (and other configured origins) for cross-origin requests
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrEmpty(origin)) return false;
                var uri = new Uri(origin);
                return (uri.Host == "localhost" || uri.Host == "127.0.0.1") &&
                       (uri.Scheme == "http" || uri.Scheme == "https");
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Add Infrastructure (DbContext, Repositories)
builder.Services.AddInfrastructure(builder.Configuration);

// Add Secrets
builder.Services.AddSecretStore(builder.Configuration, builder.Environment.IsDevelopment());

// Add LLM Services (Claude provider with Soul.md integration)
// Use placeholder in development if no API key is configured
var usePlaceholderLlm = builder.Environment.IsDevelopment() && 
    string.IsNullOrEmpty(builder.Configuration["Claude:ApiKey"]) &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
builder.Services.AddLlmServices(builder.Configuration, usePlaceholderLlm);

// Add Memory System
builder.Services.AddMemorySystem();

// Add Workflow (Kanban + Approvals + Assistant Event Loop)
builder.Services.AddWorkflowServices();

// Add Scheduler (Cron jobs + Background processing)
builder.Services.AddSchedulerServices();

// Add Skills Framework (Gmail, Calendar, Shell)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string not configured");
builder.Services.AddSkillsFramework(connectionString);

// Add Channels (WhatsApp, Telegram, Slack, Discord)
builder.Services.AddChannels(builder.Configuration);

// WhatsApp Bridge HTTP client (proxies status and QR from the bridge)
builder.Services.AddHttpClient<IWhatsAppBridgeClient, WhatsAppBridgeClient>((sp, client) =>
{
    var baseUrl = sp.GetRequiredService<IConfiguration>()["WhatsAppBridge:BaseUrl"]?.Trim();
    client.Timeout = TimeSpan.FromSeconds(5);
    if (!string.IsNullOrWhiteSpace(baseUrl))
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
});
builder.Services.AddHttpClient<GoogleOAuthProviderClient>();
builder.Services.AddSingleton<IOAuthProviderClient>(sp => sp.GetRequiredService<GoogleOAuthProviderClient>());
builder.Services.AddSingleton<IOAuthStateStore, OAuthStateStore>();
builder.Services.AddSingleton<IOAuthProviderConfigService, OAuthProviderConfigService>();
builder.Services.AddScoped<LegacyOAuthAccountMigrator>();

// Hooks (registry, executor, actions, persistence)
builder.Services.AddSingleton<IHookExecutor, HookExecutor>();
builder.Services.AddSingleton<IHookRegistry>(sp =>
{
    var executor = sp.GetRequiredService<IHookExecutor>();
    var logger = sp.GetRequiredService<ILogger<HookRegistry>>();
    var execLogger = sp.GetService<IHookExecutionLogger>();
    return new HookRegistry(executor, logger, execLogger);
});
builder.Services.AddSingleton<IHookExecutionLogger, HookExecutionLogger>();
builder.Services.AddScoped<HookService>();
builder.Services.AddHttpClient("HookHttpCallback", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddKeyedScoped<IHookAction, CreateTaskAction>("create_task");
builder.Services.AddKeyedScoped<IHookAction, SendMessageAction>("send_message");
builder.Services.AddKeyedScoped<IHookAction, HttpCallbackAction>("http_callback");
builder.Services.AddKeyedScoped<IHookAction, ExecuteSkillAction>("execute_skill");

// Feed WebSocket and broadcast
builder.Services.AddSingleton<FeedWebSocketManager>();
builder.Services.AddSingleton<IFeedBroadcastService, FeedWebSocketBroadcastService>();

// Auto-reply: listen for incoming channel messages and respond via LLM
builder.Services.AddHostedService<ChannelAutoReplyService>();

var app = builder.Build();

// Initialize skills on startup
await app.Services.InitializeSkillsAsync();

// Ensure hooks tables exist (for existing DBs that skipped init scripts)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Botty.Infrastructure.Data.BottyDbContext>();
    await MemorySchemaEnsurer.EnsureAsync(db);
    await HooksSchemaEnsurer.EnsureAsync(db);
    await scope.ServiceProvider.GetRequiredService<HookService>().LoadHooksIntoRegistryAsync();
    await scope.ServiceProvider.GetRequiredService<LegacyOAuthAccountMigrator>().MigrateAsync();
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseWebSockets();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws/feed" && context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var manager = context.RequestServices.GetRequiredService<FeedWebSocketManager>();
        await manager.AcceptConnectionAsync(webSocket, context.RequestAborted);
        return;
    }
    await next();
});

app.MapControllers();

// Health check endpoint (also under /api for frontend)
var healthPayload = new { Status = "Healthy", Timestamp = DateTime.UtcNow };
app.MapGet("/health", () => Results.Ok(healthPayload)).WithName("HealthCheck");
app.MapGet("/api/health", () => Results.Ok(healthPayload)).WithName("HealthCheckApi");

app.Run();
