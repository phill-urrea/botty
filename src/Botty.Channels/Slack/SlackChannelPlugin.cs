using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Botty.Channels.Base;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.Channels.Slack;

public class SlackChannelPlugin : BaseChannelPlugin
{
    private readonly SlackOptions _options;
    private HttpClient? _httpClient;
    private string? _botUserId;
    private string? _botUserName;
    private string? _teamId;
    private string? _teamName;
    private System.Net.WebSockets.ClientWebSocket? _socketModeClient;
    private CancellationTokenSource? _socketCts;
    
    private const string SlackApiBaseUrl = "https://slack.com/api";
    
    public override string Id => "slack";
    public override string Label => "Slack";
    public override string Description => "Slack workspace messaging";
    
    public override ChannelCapabilities Capabilities => new()
    {
        SupportsMedia = true,
        SupportsThreads = true,
        SupportsReactions = true,
        SupportsEdits = true,
        SupportsDeletes = true,
        SupportsVoiceNotes = false,
        SupportsTypingIndicator = true,
        SupportsReadReceipts = false,
        MaxMessageLength = 40000,
        SupportedMediaTypes = ["image/jpeg", "image/png", "image/gif", "video/mp4", "application/pdf"]
    };
    
    public override ChannelConfigSchema ConfigSchema => new()
    {
        ChannelId = Id,
        Fields =
        [
            new ChannelConfigField { Key = "bot_token", Label = "Bot Token", Type = ChannelConfigFieldType.Secret, IsSensitive = true, IsRequired = true },
            new ChannelConfigField { Key = "app_token", Label = "App Token", Type = ChannelConfigFieldType.Secret, IsSensitive = true }
        ]
    };
    
    public SlackChannelPlugin(IOptions<SlackOptions> options, ILogger<SlackChannelPlugin> logger) : base(logger) => _options = options.Value;
    
    protected override async Task DoInitializeAsync(ChannelConfig config, CancellationToken ct)
    {
        var botToken = await config.GetRequiredSecretAsync("bot_token", ct);
        _httpClient = new HttpClient { BaseAddress = new Uri(SlackApiBaseUrl), Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds) };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", botToken);
        
        var auth = await CallApiAsync<SlackAuthTestResponse>("auth.test", ct: ct);
        if (!auth.Ok) throw new InvalidOperationException($"Slack auth failed: {auth.Error}");
        _botUserId = auth.UserId; _botUserName = auth.User; _teamId = auth.TeamId; _teamName = auth.Team;
        Logger.LogInformation("Slack connected: @{Name} in {Team}", _botUserName, _teamName);
        
        var appToken = await config.GetSecretAsync("app_token", ct);
        if (!string.IsNullOrEmpty(appToken) && _options.UseSocketMode) await StartSocketModeAsync(appToken, ct);
    }
    
    protected override async Task DoDisconnectAsync(CancellationToken ct)
    {
        _socketCts?.Cancel();
        if (_socketModeClient != null) { try { await _socketModeClient.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Bye", ct); } catch { } _socketModeClient.Dispose(); }
        _socketCts?.Dispose(); _httpClient?.Dispose(); _httpClient = null;
    }
    
    protected override string? GetAccountId() => _botUserId;
    protected override string? GetAccountName() => _botUserName;
    
    public override async Task<SendResult> SendTextAsync(OutboundMessage msg, CancellationToken ct = default)
    {
        if (_httpClient == null) return SendResult.Failed("Not initialized");
        var payload = new Dictionary<string, object> { ["channel"] = msg.ChatId, ["text"] = msg.Text };
        if (!string.IsNullOrEmpty(msg.ThreadId)) payload["thread_ts"] = msg.ThreadId;
        var resp = await CallApiAsync<SlackPostMessageResponse>("chat.postMessage", payload, ct);
        return resp.Ok ? SendResult.Ok(resp.Ts) : SendResult.Failed(resp.Error ?? "Failed");
    }
    
    protected override async Task<SendResult> DoSendMediaAsync(OutboundMediaMessage msg, CancellationToken ct)
    {
        if (_httpClient == null) return SendResult.Failed("Not initialized");
        var upload = await CallApiAsync<SlackUploadUrlResponse>("files.getUploadURLExternal", new Dictionary<string, object> { ["filename"] = msg.FileName ?? "file", ["length"] = msg.MediaStream.Length }, ct);
        if (!upload.Ok) return SendResult.Failed(upload.Error ?? "Upload URL failed");
        using var c = new HttpClient(); using var sc = new StreamContent(msg.MediaStream); sc.Headers.ContentType = new MediaTypeHeaderValue(msg.MediaType);
        var ur = await c.PostAsync(upload.UploadUrl, sc, ct); if (!ur.IsSuccessStatusCode) return SendResult.Failed("Upload failed");
        var complete = await CallApiAsync<SlackCompleteUploadResponse>("files.completeUploadExternal", new Dictionary<string, object> { ["files"] = new[] { new { id = upload.FileId, title = msg.FileName ?? "file" } }, ["channel_id"] = msg.ChatId }, ct);
        return complete.Ok ? SendResult.Ok(complete.Files?.FirstOrDefault()?.Id) : SendResult.Failed(complete.Error ?? "Complete failed");
    }
    
    protected override async Task<SendResult> DoSendReactionAsync(string chatId, string messageId, string emoji, CancellationToken ct) =>
        (await CallApiAsync<SlackApiResponse>("reactions.add", new Dictionary<string, object> { ["channel"] = chatId, ["timestamp"] = messageId, ["name"] = emoji.Trim(':') }, ct)).Ok ? SendResult.Ok() : SendResult.Failed("Failed");
    
    public void HandleSlackEvent(SlackEventWrapper w)
    {
        if (w.Event?.Type == "message" && string.IsNullOrEmpty(w.Event.SubType) && w.Event.User != _botUserId)
            OnMessageReceived(new IncomingMessage { MessageId = w.Event.Ts ?? "", ChatId = w.Event.Channel ?? "", SenderId = w.Event.User ?? "", SenderName = w.Event.User ?? "", Text = w.Event.Text ?? "", Timestamp = DateTime.UtcNow, ChannelId = Id, Type = MessageType.Text, ThreadId = w.Event.ThreadTs });
    }
    
    private async Task StartSocketModeAsync(string appToken, CancellationToken ct)
    {
        using var c = new HttpClient(); c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appToken);
        var r = await c.PostAsync($"{SlackApiBaseUrl}/apps.connections.open", null, ct);
        var res = await r.Content.ReadFromJsonAsync<SlackSocketModeResponse>(ct);
        if (res?.Ok != true || string.IsNullOrEmpty(res.Url)) return;
        _socketCts = new(); _socketModeClient = new(); await _socketModeClient.ConnectAsync(new Uri(res.Url), _socketCts.Token);
        _ = Task.Run(async () => { var buf = new byte[8192]; while (_socketModeClient?.State == System.Net.WebSockets.WebSocketState.Open) { try { var m = await _socketModeClient.ReceiveAsync(buf, _socketCts.Token); if (m.MessageType == System.Net.WebSockets.WebSocketMessageType.Text) { var j = System.Text.Encoding.UTF8.GetString(buf, 0, m.Count); using var d = JsonDocument.Parse(j); if (d.RootElement.TryGetProperty("envelope_id", out var e)) { var a = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { envelope_id = e.GetString() })); await _socketModeClient.SendAsync(a, System.Net.WebSockets.WebSocketMessageType.Text, true, default); } if (d.RootElement.TryGetProperty("payload", out var p)) { var ev = p.Deserialize<SlackEventWrapper>(); if (ev != null) HandleSlackEvent(ev); } } } catch { break; } } });
    }
    
    private async Task<T> CallApiAsync<T>(string method, Dictionary<string, object>? payload = null, CancellationToken ct = default) where T : SlackApiResponse, new() =>
        _httpClient == null ? new T { Ok = false } : await (payload != null ? _httpClient.PostAsJsonAsync($"/{method}", payload, ct) : _httpClient.PostAsync($"/{method}", null, ct)).ContinueWith(async t => await t.Result.Content.ReadFromJsonAsync<T>(ct) ?? new T()).Unwrap();
}

public class SlackApiResponse { [JsonPropertyName("ok")] public bool Ok { get; set; } [JsonPropertyName("error")] public string? Error { get; set; } }
public class SlackAuthTestResponse : SlackApiResponse { [JsonPropertyName("user_id")] public string? UserId { get; set; } [JsonPropertyName("user")] public string? User { get; set; } [JsonPropertyName("team_id")] public string? TeamId { get; set; } [JsonPropertyName("team")] public string? Team { get; set; } }
public class SlackPostMessageResponse : SlackApiResponse { [JsonPropertyName("ts")] public string? Ts { get; set; } }
public class SlackUploadUrlResponse : SlackApiResponse { [JsonPropertyName("upload_url")] public string? UploadUrl { get; set; } [JsonPropertyName("file_id")] public string? FileId { get; set; } }
public class SlackCompleteUploadResponse : SlackApiResponse { [JsonPropertyName("files")] public List<SlackFile>? Files { get; set; } }
public class SlackFile { [JsonPropertyName("id")] public string? Id { get; set; } }
public class SlackSocketModeResponse : SlackApiResponse { [JsonPropertyName("url")] public string? Url { get; set; } }
public class SlackEventWrapper { [JsonPropertyName("event")] public SlackEvent? Event { get; set; } }
public class SlackEvent { [JsonPropertyName("type")] public string? Type { get; set; } [JsonPropertyName("subtype")] public string? SubType { get; set; } [JsonPropertyName("user")] public string? User { get; set; } [JsonPropertyName("text")] public string? Text { get; set; } [JsonPropertyName("ts")] public string? Ts { get; set; } [JsonPropertyName("channel")] public string? Channel { get; set; } [JsonPropertyName("thread_ts")] public string? ThreadTs { get; set; } }
