using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Skills.Base;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using GmailMessage = Google.Apis.Gmail.v1.Data.Message;

namespace Botty.Skills.Gmail;

/// <summary>
/// Skill for Gmail integration supporting multiple accounts.
/// </summary>
public class GmailSkill : BaseSkill
{
    private readonly Dictionary<string, GmailService> _services = new();
    private readonly ILinkedAccountService _linkedAccountService;
    
    public GmailSkill(
        ILinkedAccountService linkedAccountService,
        ILogger<GmailSkill> logger) : base(logger)
    {
        _linkedAccountService = linkedAccountService;
    }

    public override string Id => "gmail";
    public override string Name => "Gmail";
    public override string Description => "Access and manage multiple Gmail accounts - read, send, and search emails.";

    public override SkillConfigSchema ConfigSchema => new()
    {
        SkillId = Id,
        Fields =
        [
            new ConfigField
            {
                Key = "client_id",
                Label = "OAuth Client ID",
                Description = "DEPRECATED: managed via Settings OAuth provider config.",
                Type = ConfigFieldType.String,
                IsSensitive = true,
                IsRequired = false
            },
            new ConfigField
            {
                Key = "client_secret",
                Label = "OAuth Client Secret",
                Description = "DEPRECATED: managed via Settings OAuth provider config.",
                Type = ConfigFieldType.String,
                IsSensitive = true,
                IsRequired = false
            },
            new ConfigField
            {
                Key = "accounts",
                Label = "Configured Accounts",
                Description = "DEPRECATED: legacy JSON accounts with refresh tokens. Use OAuth linked accounts in Settings.",
                Type = ConfigFieldType.Json,
                IsSensitive = true,
                IsRequired = false
            },
            new ConfigField
            {
                Key = "default_account",
                Label = "Default Account",
                Description = "Email address of the default account to use",
                Type = ConfigFieldType.String,
                IsSensitive = false,
                IsRequired = false
            }
        ]
    };

    protected override async Task OnInitializeAsync(CancellationToken ct)
    {
        _services.Clear();

        var clientId = GetConfig("client_id");
        var clientSecret = GetConfig("client_secret");
        var accountsJson = GetConfig("accounts"); // legacy fallback

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            Logger.LogWarning("Gmail skill: OAuth credentials not configured");
            return;
        }

        try
        {
            var linkedAccounts = await _linkedAccountService.GetByProviderAsync(OAuthProviderType.Google, ct);
            if (linkedAccounts.Count > 0)
            {
                foreach (var linked in linkedAccounts)
                {
                    var credential = await _linkedAccountService.GetCredentialByProviderAndEmailAsync(
                        OAuthProviderType.Google,
                        linked.Email,
                        ct);
                    if (credential == null)
                        continue;

                    try
                    {
                        var account = new GmailAccountConfig
                        {
                            Email = linked.Email,
                            RefreshToken = credential.RefreshToken,
                            AccessToken = credential.AccessToken
                        };

                        var userCredential = await CreateCredentialAsync(clientId, clientSecret, account, ct);
                        var service = new GmailService(new BaseClientService.Initializer
                        {
                            HttpClientInitializer = userCredential,
                            ApplicationName = "Botty Assistant"
                        });

                        _services[account.Email] = service;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Gmail: Failed to connect linked account {Email}", linked.Email);
                    }
                }

                if (_services.Count > 0)
                {
                    Logger.LogInformation("Gmail: initialized {Count} linked account(s)", _services.Count);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Gmail: linked account initialization failed, falling back to legacy accounts");
        }

        if (string.IsNullOrEmpty(accountsJson))
        {
            Logger.LogWarning("Gmail skill: No linked or legacy accounts configured");
            return;
        }

        try
        {
            var accounts = ParseAccounts(accountsJson);
            if (accounts.Count == 0)
            {
                Logger.LogWarning("Gmail skill: Accounts config parsed but contains no valid accounts");
                return;
            }

            foreach (var account in accounts)
            {
                if (string.IsNullOrWhiteSpace(account.Email) || string.IsNullOrWhiteSpace(account.RefreshToken))
                {
                    Logger.LogWarning("Gmail: Skipping account with missing email or refresh token");
                    continue;
                }

                try
                {
                    var credential = await CreateCredentialAsync(clientId, clientSecret, account, ct);
                    var service = new GmailService(new BaseClientService.Initializer
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = "Botty Assistant"
                    });
                    
                    _services[account.Email] = service;
                    Logger.LogInformation("Gmail: Connected account {Email}", account.Email);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Gmail: Failed to connect account {Email}", account.Email);
                }
            }
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Gmail: Failed to parse accounts configuration");
        }
    }

    private static List<GmailAccountConfig> ParseAccounts(string accountsJson)
    {
        // Accept either:
        // 1) an array: [{ "email": "...", "refreshToken": "..." }]
        // 2) an object wrapper: { "accounts": [ ... ] }
        using var doc = JsonDocument.Parse(accountsJson);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<GmailAccountConfig>>(accountsJson) ?? [];
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("accounts", out var accountsElement) &&
            accountsElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<GmailAccountConfig>>(accountsElement.GetRawText()) ?? [];
        }

        return [];
    }

    private static async Task<UserCredential> CreateCredentialAsync(
        string clientId, 
        string clientSecret, 
        GmailAccountConfig account,
        CancellationToken ct)
    {
        var secrets = new ClientSecrets
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };

        // Create credential from refresh token
        var token = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
        {
            RefreshToken = account.RefreshToken,
            AccessToken = account.AccessToken,
            ExpiresInSeconds = 3600
        };

        var flow = new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow(
            new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets,
                Scopes = new[] 
                { 
                    GmailService.Scope.GmailReadonly,
                    GmailService.Scope.GmailSend,
                    GmailService.Scope.GmailModify
                }
            });

        return new UserCredential(flow, account.Email, token);
    }

    public override IEnumerable<LlmTool> GetTools()
    {
        return new[]
        {
            new LlmTool
            {
                Name = "gmail_list_messages",
                Description = "List recent emails from a Gmail account",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "account": { "type": "string", "description": "Email account to use (optional, uses default if not specified)" },
                        "query": { "type": "string", "description": "Gmail search query (e.g., 'is:unread', 'from:someone@example.com')" },
                        "maxResults": { "type": "integer", "description": "Maximum number of messages to return (default: 10)" }
                    },
                    "required": []
                }
                """
            },
            new LlmTool
            {
                Name = "gmail_get_message",
                Description = "Get the full content of a specific email",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "account": { "type": "string", "description": "Email account to use" },
                        "messageId": { "type": "string", "description": "The ID of the message to retrieve" }
                    },
                    "required": ["messageId"]
                }
                """
            },
            new LlmTool
            {
                Name = "gmail_send_message",
                Description = "Send an email (requires approval)",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "account": { "type": "string", "description": "Email account to send from" },
                        "to": { "type": "string", "description": "Recipient email address" },
                        "subject": { "type": "string", "description": "Email subject" },
                        "body": { "type": "string", "description": "Email body (plain text or HTML)" },
                        "isHtml": { "type": "boolean", "description": "Whether the body is HTML" }
                    },
                    "required": ["to", "subject", "body"]
                }
                """
            },
            new LlmTool
            {
                Name = "gmail_list_accounts",
                Description = "List all configured Gmail accounts",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {},
                    "required": []
                }
                """
            }
        };
    }

    protected override async Task<SkillResult> OnExecuteAsync(SkillContext context, CancellationToken ct)
    {
        return context.ToolName switch
        {
            "gmail_list_messages" => await ListMessagesAsync(context.Arguments, ct),
            "gmail_get_message" => await GetMessageAsync(context.Arguments, ct),
            "gmail_send_message" => await SendMessageAsync(context.Arguments, ct),
            "gmail_list_accounts" => ListAccounts(),
            _ => SkillResult.Fail($"Unknown tool: {context.ToolName}")
        };
    }

    private async Task<SkillResult> ListMessagesAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<ListMessagesArgs>(arguments);
        if (args == null) return SkillResult.Fail("Invalid arguments");

        var service = GetService(args.Account);
        if (service == null) return SkillResult.Fail("No Gmail account configured or specified");

        var request = service.Users.Messages.List("me");
        request.MaxResults = args.MaxResults ?? 10;
        if (!string.IsNullOrEmpty(args.Query))
        {
            request.Q = args.Query;
        }

        var response = await request.ExecuteAsync(ct);
        
        if (response.Messages == null || response.Messages.Count == 0)
        {
            return SkillResult.Ok(ToJson(new { messages = Array.Empty<object>(), count = 0 }));
        }

        var messages = new List<object>();
        foreach (var msg in response.Messages.Take(args.MaxResults ?? 10))
        {
            var detail = await service.Users.Messages.Get("me", msg.Id).ExecuteAsync(ct);
            messages.Add(new
            {
                id = msg.Id,
                threadId = msg.ThreadId,
                subject = GetHeader(detail, "Subject"),
                from = GetHeader(detail, "From"),
                to = GetHeader(detail, "To"),
                date = GetHeader(detail, "Date"),
                snippet = detail.Snippet,
                labels = detail.LabelIds
            });
        }

        return SkillResult.Ok(ToJson(new { messages, count = messages.Count }));
    }

    private async Task<SkillResult> GetMessageAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<GetMessageArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.MessageId))
            return SkillResult.Fail("Invalid arguments: messageId required");

        var service = GetService(args.Account);
        if (service == null) return SkillResult.Fail("No Gmail account configured or specified");

        var message = await service.Users.Messages.Get("me", args.MessageId).ExecuteAsync(ct);
        
        var body = GetMessageBody(message);

        return SkillResult.Ok(ToJson(new
        {
            id = message.Id,
            threadId = message.ThreadId,
            subject = GetHeader(message, "Subject"),
            from = GetHeader(message, "From"),
            to = GetHeader(message, "To"),
            cc = GetHeader(message, "Cc"),
            date = GetHeader(message, "Date"),
            body,
            labels = message.LabelIds
        }));
    }

    private async Task<SkillResult> SendMessageAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<SendMessageArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.To) || string.IsNullOrEmpty(args.Subject))
            return SkillResult.Fail("Invalid arguments: to and subject required");

        var service = GetService(args.Account);
        if (service == null) return SkillResult.Fail("No Gmail account configured or specified");

        var accountEmail = args.Account ?? GetDefaultAccount();

        // Build the email
        var email = new StringBuilder();
        email.AppendLine($"From: {accountEmail}");
        email.AppendLine($"To: {args.To}");
        email.AppendLine($"Subject: {args.Subject}");
        email.AppendLine(args.IsHtml ? "Content-Type: text/html; charset=utf-8" : "Content-Type: text/plain; charset=utf-8");
        email.AppendLine();
        email.AppendLine(args.Body);

        var message = new GmailMessage
        {
            Raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(email.ToString()))
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "")
        };

        var result = await service.Users.Messages.Send(message, "me").ExecuteAsync(ct);

        return SkillResult.Ok(ToJson(new
        {
            success = true,
            messageId = result.Id,
            threadId = result.ThreadId
        }));
    }

    private SkillResult ListAccounts()
    {
        var accounts = _services.Keys.Select(email => new
        {
            email,
            isDefault = email == GetDefaultAccount()
        }).ToList();

        return SkillResult.Ok(ToJson(new { accounts, count = accounts.Count }));
    }

    private GmailService? GetService(string? account)
    {
        if (!string.IsNullOrEmpty(account) && _services.TryGetValue(account, out var service))
        {
            return service;
        }

        var defaultAccount = GetDefaultAccount();
        if (!string.IsNullOrEmpty(defaultAccount) && _services.TryGetValue(defaultAccount, out var defaultService))
        {
            return defaultService;
        }

        return _services.Values.FirstOrDefault();
    }

    private string? GetDefaultAccount()
    {
        return GetConfig("default_account");
    }

    private static string? GetHeader(GmailMessage message, string name)
    {
        return message.Payload?.Headers?.FirstOrDefault(h => 
            string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static string GetMessageBody(GmailMessage message)
    {
        // Try to get plain text body first
        var body = GetBodyFromParts(message.Payload, "text/plain");
        if (string.IsNullOrEmpty(body))
        {
            body = GetBodyFromParts(message.Payload, "text/html");
        }
        
        if (string.IsNullOrEmpty(body) && message.Payload?.Body?.Data != null)
        {
            body = DecodeBase64(message.Payload.Body.Data);
        }

        return body ?? "";
    }

    private static string? GetBodyFromParts(Google.Apis.Gmail.v1.Data.MessagePart? part, string mimeType)
    {
        if (part == null) return null;

        if (part.MimeType == mimeType && part.Body?.Data != null)
        {
            return DecodeBase64(part.Body.Data);
        }

        if (part.Parts != null)
        {
            foreach (var subPart in part.Parts)
            {
                var body = GetBodyFromParts(subPart, mimeType);
                if (!string.IsNullOrEmpty(body)) return body;
            }
        }

        return null;
    }

    private static string DecodeBase64(string data)
    {
        var bytes = Convert.FromBase64String(data.Replace('-', '+').Replace('_', '/'));
        return Encoding.UTF8.GetString(bytes);
    }

    // Argument classes
    private class ListMessagesArgs
    {
        public string? Account { get; set; }
        public string? Query { get; set; }
        public int? MaxResults { get; set; }
    }

    private class GetMessageArgs
    {
        public string? Account { get; set; }
        public string? MessageId { get; set; }
    }

    private class SendMessageArgs
    {
        public string? Account { get; set; }
        public string? To { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public bool IsHtml { get; set; }
    }
}

/// <summary>
/// Configuration for a Gmail account.
/// </summary>
public class GmailAccountConfig
{
    public required string Email { get; set; }
    public required string RefreshToken { get; set; }
    public string? AccessToken { get; set; }
}
