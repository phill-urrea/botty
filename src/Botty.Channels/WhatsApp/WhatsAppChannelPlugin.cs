using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Botty.Channels.Base;
using Botty.Channels.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.Channels.WhatsApp;

/// <summary>
/// WhatsApp channel plugin that wraps the Node.js WhatsApp bridge
/// </summary>
public class WhatsAppChannelPlugin : BaseChannelPlugin
{
    private readonly WhatsAppOptions _options;
    private readonly IMessageChunkingService _chunking;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<string, WhatsAppAccountConnection> _accounts = new();
    // Legacy single-account fields (used when no Accounts config is present)
    private HttpClient? _httpClient;
    private string? _accountId;
    private string? _accountName;
    private Timer? _healthCheckTimer;
    
    public override string Id => "whatsapp";
    public override string Label => "WhatsApp";
    public override string Description => "WhatsApp messaging via whatsapp-web.js bridge";
    
    public override ChannelCapabilities Capabilities => new()
    {
        SupportsMedia = true,
        SupportsThreads = false,
        SupportsReactions = true,
        SupportsEdits = false,
        SupportsDeletes = true,
        SupportsVoiceNotes = true,
        SupportsTypingIndicator = true,
        SupportsReadReceipts = true,
        SupportsPolls = true,
        MaxPollOptions = 12,
        MaxMessageLength = 65536,
        SupportedMediaTypes = [
            "image/jpeg", "image/png", "image/gif", "image/webp",
            "video/mp4", "video/3gpp",
            "audio/ogg", "audio/mpeg", "audio/aac",
            "application/pdf", "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        ]
    };
    
    public override ChannelConfigSchema ConfigSchema => new()
    {
        ChannelId = Id,
        Fields =
        [
            new ChannelConfigField
            {
                Key = "bridge_url",
                Label = "Bridge URL",
                Description = "URL of the WhatsApp Node.js bridge",
                Type = ChannelConfigFieldType.String,
                DefaultValue = "http://localhost:3001"
            },
            new ChannelConfigField
            {
                Key = "auto_reconnect",
                Label = "Auto Reconnect",
                Description = "Automatically reconnect on disconnection",
                Type = ChannelConfigFieldType.Boolean,
                DefaultValue = "true"
            }
        ]
    };
    
    public WhatsAppChannelPlugin(
        IOptions<WhatsAppOptions> options,
        IMessageChunkingService chunking,
        IServiceScopeFactory scopeFactory,
        ILogger<WhatsAppChannelPlugin> logger)
        : base(logger)
    {
        _options = options.Value;
        _chunking = chunking;
        _scopeFactory = scopeFactory;
    }
    
    protected override async Task DoInitializeAsync(ChannelConfig config, CancellationToken ct)
    {
        // Multi-account initialization
        if (_options.Accounts.Count > 0)
        {
            foreach (var (accountId, accountOpts) in _options.Accounts)
            {
                await InitializeAccountAsync(accountId, accountOpts, ct);
            }

            // Set default account as the primary for legacy methods
            var defaultId = _options.DefaultAccount;
            if (_accounts.TryGetValue(defaultId, out var defaultConn))
            {
                _httpClient = defaultConn.HttpClient;
                _accountId = defaultConn.PhoneNumber;
                _accountName = defaultConn.AccountName;
            }

            Logger.LogInformation("WhatsApp multi-account initialized with {Count} accounts", _accounts.Count);
        }
        else
        {
            // Single-account (legacy) initialization
            var bridgeUrl = config.GetValue("bridge_url", _options.BridgeUrl);

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(bridgeUrl),
                Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
            };

            // Retry status check — the bridge may still be authenticating on startup
            WhatsAppBridgeStatus? status = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                status = await GetBridgeStatusAsync(ct);
                if (status.IsConnected) break;
                Logger.LogDebug("WhatsApp bridge not ready (attempt {Attempt}/10), retrying in 3s…", attempt + 1);
                await Task.Delay(3000, ct);
            }

            if (status is not { IsConnected: true })
            {
                throw new InvalidOperationException(
                    "WhatsApp bridge is not connected. Please scan the QR code to connect.");
            }

            _accountId = status.PhoneNumber;
            _accountName = status.AccountName;

            if (_options.HealthCheckIntervalSeconds > 0)
            {
                _healthCheckTimer = new Timer(
                    async _ => await HealthCheckAsync(),
                    null,
                    TimeSpan.FromSeconds(_options.HealthCheckIntervalSeconds),
                    TimeSpan.FromSeconds(_options.HealthCheckIntervalSeconds));
            }

            Logger.LogInformation(
                "WhatsApp channel initialized. Connected as {AccountName} ({PhoneNumber})",
                _accountName, _accountId);
        }
    }

    private async Task InitializeAccountAsync(string accountId, WhatsAppAccountOptions opts, CancellationToken ct)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(opts.BridgeUrl),
            Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds)
        };

        var conn = new WhatsAppAccountConnection
        {
            AccountId = accountId,
            Options = opts,
            HttpClient = httpClient
        };

        try
        {
            var response = await httpClient.GetAsync("/status", ct);
            if (response.IsSuccessStatusCode)
            {
                var status = await response.Content.ReadFromJsonAsync<WhatsAppBridgeStatus>(ct);
                if (status?.IsConnected == true)
                {
                    conn.PhoneNumber = status.PhoneNumber;
                    conn.AccountName = status.AccountName;
                    conn.IsConnected = true;
                    conn.ConnectedSince = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            conn.LastError = ex.Message;
            Logger.LogWarning(ex, "Failed to initialize WhatsApp account {AccountId}", accountId);
        }

        // Start per-account health check
        conn.HealthCheckTimer = new Timer(
            async _ => await AccountHealthCheckAsync(accountId),
            null,
            TimeSpan.FromSeconds(_options.HealthCheckIntervalSeconds),
            TimeSpan.FromSeconds(_options.HealthCheckIntervalSeconds));

        _accounts[accountId] = conn;

        Logger.LogInformation("WhatsApp account {AccountId} initialized (connected={Connected})",
            accountId, conn.IsConnected);
    }
    
    protected override async Task DoDisconnectAsync(CancellationToken ct)
    {
        // Disconnect all multi-account connections
        foreach (var conn in _accounts.Values)
        {
            conn.Dispose();
        }
        _accounts.Clear();

        // Disconnect legacy single-account
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;

        if (_httpClient != null)
        {
            try
            {
                await _httpClient.PostAsync("/disconnect", null, ct);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error disconnecting from WhatsApp bridge");
            }

            _httpClient.Dispose();
            _httpClient = null;
        }

        _accountId = null;
        _accountName = null;
    }
    
    protected override string? GetAccountId() => _accountId;
    protected override string? GetAccountName() => _accountName;
    
    public override async Task<SendResult> SendTextAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (_httpClient == null)
        {
            return SendResult.Failed("Channel not initialized");
        }

        try
        {
            // Chunk the message if it exceeds the limit
            var chunks = _chunking.Chunk(
                message.Text,
                _options.ChunkLimit > 0 ? _options.ChunkLimit : null,
                !string.IsNullOrEmpty(_options.ChunkMode) ? _options.ChunkMode : null);

            string? lastMessageId = null;

            for (var i = 0; i < chunks.Count; i++)
            {
                var request = new
                {
                    to = message.ChatId,
                    body = chunks[i],
                    // Only reply to original message for first chunk
                    replyTo = i == 0 ? message.ReplyToMessageId : null
                };

                var response = await _httpClient.PostAsJsonAsync("/send", request, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    return SendResult.Failed($"Failed to send message chunk {i + 1}/{chunks.Count}: {error}");
                }

                var result = await response.Content.ReadFromJsonAsync<WhatsAppSendResponse>(ct);
                lastMessageId = result?.MessageId;

                // Delay between chunks to avoid rate limiting
                if (i < chunks.Count - 1)
                {
                    await Task.Delay(200, ct);
                }
            }

            return SendResult.Ok(lastMessageId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending WhatsApp message to {ChatId}", message.ChatId);
            return SendResult.Failed(ex.Message);
        }
    }
    
    protected override async Task<SendResult> DoSendMediaAsync(OutboundMediaMessage message, CancellationToken ct)
    {
        if (_httpClient == null)
        {
            return SendResult.Failed("Channel not initialized");
        }
        
        try
        {
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(message.MediaStream);
            
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(message.MediaType);
            content.Add(streamContent, "media", message.FileName ?? "media");
            content.Add(new StringContent(message.ChatId), "to");
            
            if (!string.IsNullOrEmpty(message.Caption))
            {
                content.Add(new StringContent(message.Caption), "caption");
            }
            
            var response = await _httpClient.PostAsync("/send-media", content, ct);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<WhatsAppSendResponse>(ct);
                return SendResult.Ok(result?.MessageId);
            }
            
            var error = await response.Content.ReadAsStringAsync(ct);
            return SendResult.Failed($"Failed to send media: {error}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending WhatsApp media to {ChatId}", message.ChatId);
            return SendResult.Failed(ex.Message);
        }
    }
    
    protected override async Task<SendResult> DoSendReactionAsync(string chatId, string messageId, string emoji, CancellationToken ct)
    {
        if (_httpClient == null)
        {
            return SendResult.Failed("Channel not initialized");
        }
        
        try
        {
            var request = new { chatId, messageId, emoji };
            var response = await _httpClient.PostAsJsonAsync("/react", request, ct);
            
            if (response.IsSuccessStatusCode)
            {
                return SendResult.Ok();
            }
            
            var error = await response.Content.ReadAsStringAsync(ct);
            return SendResult.Failed($"Failed to send reaction: {error}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending WhatsApp reaction");
            return SendResult.Failed(ex.Message);
        }
    }
    
    protected override async Task<SendResult> DoSendPollAsync(OutboundPollMessage message, CancellationToken ct)
    {
        if (_httpClient == null)
        {
            return SendResult.Failed("Channel not initialized");
        }

        try
        {
            var request = new
            {
                to = message.ChatId,
                question = message.Poll.Question,
                options = message.Poll.Options,
                maxSelections = message.Poll.MaxSelections,
                replyTo = message.ReplyToMessageId
            };

            var response = await _httpClient.PostAsJsonAsync("/send-poll", request, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<WhatsAppSendResponse>(ct);
                return SendResult.Ok(result?.MessageId);
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            return SendResult.Failed($"Failed to send poll: {error}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending WhatsApp poll to {ChatId}", message.ChatId);
            return SendResult.Failed(ex.Message);
        }
    }

    protected override async Task DoSendTypingIndicatorAsync(string chatId, CancellationToken ct)
    {
        if (_httpClient == null) return;
        try
        {
            await _httpClient.PostAsJsonAsync("/typing", new { chatId }, ct);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to send typing indicator to {ChatId}", chatId);
        }
    }

    /// <summary>
    /// Handle an incoming message from the bridge webhook.
    /// Applies security filter and mention gating before raising the event.
    /// </summary>
    public async Task HandleIncomingMessageAsync(WhatsAppIncomingMessage message, CancellationToken ct = default)
    {
        var incomingMessage = new IncomingMessage
        {
            MessageId = message.MessageId,
            ChatId = message.From,
            SenderId = message.From,
            SenderName = message.FromName,
            Text = message.Body,
            Timestamp = message.Timestamp,
            ChannelId = Id,
            Type = MapMessageType(message.Type),
            MediaUrl = message.MediaUrl,
            MediaType = message.MediaType,
            Metadata = new Dictionary<string, object>
            {
                ["isGroup"] = message.IsGroup,
                ["groupName"] = message.GroupName ?? string.Empty,
                ["conversationId"] = message.ConversationId ?? string.Empty
            }
        };

        // Apply security filter
        var decision = await FilterIncomingWithScopeAsync(incomingMessage, ct);

        switch (decision)
        {
            case SecurityDecision.Deny:
                Logger.LogInformation("Security filter denied message from {SenderId}", message.From);
                return;

            case SecurityDecision.RequestPairing:
                Logger.LogInformation("Security filter requires pairing for {SenderId}", message.From);
                var code = await GeneratePairingCodeWithScopeAsync(message.From, ct);
                if (code != null)
                {
                    await SendTextAsync(new OutboundMessage(
                        message.From,
                        $"Hi! I don't recognize you yet. To pair with me, ask my owner to approve code: *{code}*\n\nThis code expires in 1 hour."),
                        ct);
                }
                return;

            case SecurityDecision.Allow:
            default:
                break;
        }

        // Send typing indicator immediately so the user sees feedback while the message routes
        _ = SendTypingIndicatorAsync(message.From, ct);

        // Apply mention gating for groups
        if (message.IsGroup)
        {
            var groupConfig = _options.Security.Groups.GetValueOrDefault(message.From);
            var requireMention = groupConfig?.RequireMention ?? false;

            if (requireMention)
            {
                var mentionResult = WhatsAppMentionProcessor.Process(
                    incomingMessage.Text, _accountId, _accountName);

                var gating = MentionGatingService.Resolve(
                    requireMention: true,
                    canDetect: true,
                    wasMentioned: mentionResult.WasMentioned,
                    bypass: false);

                if (gating.ShouldSkip)
                {
                    Logger.LogDebug("Mention gating skipped message from {SenderId} in group", message.From);
                    return;
                }

                // Use cleaned text (with mention stripped)
                incomingMessage = incomingMessage with { Text = mentionResult.CleanedText };
            }
        }

        OnMessageReceived(incomingMessage);
    }

    private async Task<SecurityDecision> FilterIncomingWithScopeAsync(IncomingMessage message, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var filter = scope.ServiceProvider.GetRequiredService<WhatsAppSecurityFilter>();
        return await filter.FilterIncomingAsync(message, ct);
    }

    private async Task<string?> GeneratePairingCodeWithScopeAsync(string senderId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var pairingService = scope.ServiceProvider.GetRequiredService<PairingService>();
        return await pairingService.GenerateCodeAsync("whatsapp", senderId, ct);
    }

    /// <summary>
    /// Handle an incoming message from the bridge webhook (sync wrapper for backward compatibility).
    /// </summary>
    public void HandleIncomingMessage(WhatsAppIncomingMessage message)
    {
        HandleIncomingMessageAsync(message).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Gets status for all accounts (multi-account mode).
    /// </summary>
    public Dictionary<string, WhatsAppAccountStatus> GetAllAccountStatuses()
    {
        var statuses = new Dictionary<string, WhatsAppAccountStatus>();
        foreach (var (id, conn) in _accounts)
        {
            statuses[id] = new WhatsAppAccountStatus
            {
                AccountId = id,
                IsConnected = conn.IsConnected,
                PhoneNumber = conn.PhoneNumber,
                AccountName = conn.AccountName,
                ConnectedSince = conn.ConnectedSince,
                LastError = conn.LastError
            };
        }

        // If no multi-account, report single account
        if (statuses.Count == 0 && _httpClient != null)
        {
            statuses["default"] = new WhatsAppAccountStatus
            {
                AccountId = "default",
                IsConnected = IsInitialized && string.IsNullOrEmpty(LastError),
                PhoneNumber = _accountId,
                AccountName = _accountName,
                ConnectedSince = ConnectedSince,
                LastError = LastError
            };
        }

        return statuses;
    }

    /// <summary>
    /// Gets the HTTP client for a specific account, falling back to default.
    /// </summary>
    public HttpClient? GetClientForAccount(string? accountId)
    {
        if (!string.IsNullOrEmpty(accountId) && _accounts.TryGetValue(accountId, out var conn))
            return conn.HttpClient;

        // Try default account
        if (_accounts.TryGetValue(_options.DefaultAccount, out var defaultConn))
            return defaultConn.HttpClient;

        return _httpClient;
    }

    private async Task AccountHealthCheckAsync(string accountId)
    {
        if (!_accounts.TryGetValue(accountId, out var conn) || conn.HttpClient == null)
            return;

        try
        {
            var response = await conn.HttpClient.GetAsync("/status");
            if (response.IsSuccessStatusCode)
            {
                var status = await response.Content.ReadFromJsonAsync<WhatsAppBridgeStatus>();
                if (status?.IsConnected == true)
                {
                    if (!conn.IsConnected)
                    {
                        conn.IsConnected = true;
                        conn.ConnectedSince = DateTime.UtcNow;
                        conn.LastError = null;
                    }
                }
                else if (conn.IsConnected)
                {
                    conn.IsConnected = false;
                    conn.LastError = "Bridge disconnected";
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Health check failed for WhatsApp account {AccountId}", accountId);
            conn.LastError = ex.Message;
        }
    }

    private async Task<WhatsAppBridgeStatus> GetBridgeStatusAsync(CancellationToken ct)
    {
        if (_httpClient == null)
        {
            return new WhatsAppBridgeStatus { IsConnected = false };
        }
        
        try
        {
            var response = await _httpClient.GetAsync("/status", ct);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<WhatsAppBridgeStatus>(ct)
                    ?? new WhatsAppBridgeStatus { IsConnected = false };
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get WhatsApp bridge status");
        }
        
        return new WhatsAppBridgeStatus { IsConnected = false };
    }
    
    private async Task HealthCheckAsync()
    {
        try
        {
            var status = await GetBridgeStatusAsync(CancellationToken.None);
            
            if (!status.IsConnected && IsInitialized)
            {
                SetError("WhatsApp bridge disconnected");
                OnEventReceived(ChannelEvent.Disconnected(Id, "Bridge disconnected"));
            }
            else if (status.IsConnected && !string.IsNullOrEmpty(LastError))
            {
                ClearError();
                OnEventReceived(ChannelEvent.Connected(Id));
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "WhatsApp health check failed");
        }
    }
    
    private static MessageType MapMessageType(string? type) => type?.ToLowerInvariant() switch
    {
        "image" => MessageType.Image,
        "video" => MessageType.Video,
        "audio" => MessageType.Audio,
        "voice" or "ptt" => MessageType.Voice,
        "document" => MessageType.Document,
        "location" => MessageType.Location,
        "contact" or "vcard" => MessageType.Contact,
        "sticker" => MessageType.Sticker,
        _ => MessageType.Text
    };
}

#region DTOs

public class WhatsAppBridgeStatus
{
    [JsonPropertyName("isReady")]
    public bool IsConnected { get; set; }
    public string? PhoneNumber { get; set; }
    public string? AccountName { get; set; }
    public string? QrCode { get; set; }
    public string? Error { get; set; }
}

public class WhatsAppSendResponse
{
    public string? MessageId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class WhatsAppAccountStatus
{
    public required string AccountId { get; set; }
    public bool IsConnected { get; set; }
    public string? PhoneNumber { get; set; }
    public string? AccountName { get; set; }
    public DateTime? ConnectedSince { get; set; }
    public string? LastError { get; set; }
}

public class WhatsAppIncomingMessage
{
    public required string MessageId { get; set; }
    public required string From { get; set; }
    public required string FromName { get; set; }
    public required string Body { get; set; }
    public required DateTime Timestamp { get; set; }
    public string? Type { get; set; }
    public bool IsGroup { get; set; }
    public string? GroupName { get; set; }
    public string? ConversationId { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaType { get; set; }
}

#endregion
