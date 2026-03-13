using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Base;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Botty.Tools.Calendar;

/// <summary>
/// Tool for Google Calendar integration supporting multiple accounts.
/// </summary>
public class GoogleCalendarTool : BaseTool
{
    private readonly Dictionary<string, CalendarService> _services = new();
    private readonly ILinkedAccountService _linkedAccountService;
    
    public GoogleCalendarTool(
        ILinkedAccountService linkedAccountService,
        ILogger<GoogleCalendarTool> logger) : base(logger)
    {
        _linkedAccountService = linkedAccountService;
    }

    public override string Id => "google-calendar";
    public override string Name => "Google Calendar";
    public override string Description => "Access and manage multiple Google Calendar accounts - view, create, and modify events.";

    public override ToolConfigSchema ConfigSchema => new()
    {
        ToolId = Id,
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
            },
            new ConfigField
            {
                Key = "default_calendar",
                Label = "Default Calendar",
                Description = "Default calendar ID to use (usually 'primary')",
                Type = ConfigFieldType.String,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "primary"
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
            Logger.LogWarning("Google Calendar tool: OAuth credentials not configured");
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
                        var account = new CalendarAccountConfig
                        {
                            Email = linked.Email,
                            RefreshToken = credential.RefreshToken,
                            AccessToken = credential.AccessToken
                        };
                        var userCredential = await CreateCredentialAsync(clientId, clientSecret, account, ct);
                        var service = new CalendarService(new BaseClientService.Initializer
                        {
                            HttpClientInitializer = userCredential,
                            ApplicationName = "Botty Assistant"
                        });

                        _services[account.Email] = service;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Google Calendar: Failed to connect linked account {Email}", linked.Email);
                    }
                }

                if (_services.Count > 0)
                {
                    Logger.LogInformation("Google Calendar: initialized {Count} linked account(s)", _services.Count);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Google Calendar: linked account initialization failed, falling back to legacy accounts");
        }

        if (string.IsNullOrEmpty(accountsJson))
        {
            Logger.LogWarning("Google Calendar tool: No linked or legacy accounts configured");
            return;
        }

        try
        {
            var accounts = ParseAccounts(accountsJson);
            if (accounts == null) return;

            foreach (var account in accounts)
            {
                try
                {
                    var credential = await CreateCredentialAsync(clientId, clientSecret, account, ct);
                    var service = new CalendarService(new BaseClientService.Initializer
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = "Botty Assistant"
                    });
                    
                    _services[account.Email] = service;
                    Logger.LogInformation("Google Calendar: Connected account {Email}", account.Email);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Google Calendar: Failed to connect account {Email}", account.Email);
                }
            }
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Google Calendar: Failed to parse accounts configuration");
        }
    }

    private static List<CalendarAccountConfig> ParseAccounts(string accountsJson)
    {
        using var doc = JsonDocument.Parse(accountsJson);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<CalendarAccountConfig>>(accountsJson) ?? [];
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("accounts", out var accountsElement) &&
            accountsElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<CalendarAccountConfig>>(accountsElement.GetRawText()) ?? [];
        }

        return [];
    }

    private static async Task<UserCredential> CreateCredentialAsync(
        string clientId, 
        string clientSecret, 
        CalendarAccountConfig account,
        CancellationToken ct)
    {
        var secrets = new ClientSecrets
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };

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
                    CalendarService.Scope.Calendar,
                    CalendarService.Scope.CalendarEvents
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
                Name = "calendar_list_events",
                Description = "List upcoming calendar events",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "account": { "type": "string", "description": "Email account to use" },
                        "calendarId": { "type": "string", "description": "Calendar ID (default: 'primary')" },
                        "timeMin": { "type": "string", "description": "Start time in ISO 8601 format" },
                        "timeMax": { "type": "string", "description": "End time in ISO 8601 format" },
                        "maxResults": { "type": "integer", "description": "Maximum events to return (default: 10)" }
                    },
                    "required": []
                }
                """
            },
            new LlmTool
            {
                Name = "calendar_get_event",
                Description = "Get details of a specific calendar event",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "account": { "type": "string", "description": "Email account to use" },
                        "calendarId": { "type": "string", "description": "Calendar ID" },
                        "eventId": { "type": "string", "description": "Event ID" }
                    },
                    "required": ["eventId"]
                }
                """
            },
            new LlmTool
            {
                Name = "calendar_create_event",
                Description = "Create a new calendar event (requires approval)",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "account": { "type": "string", "description": "Email account to use" },
                        "calendarId": { "type": "string", "description": "Calendar ID" },
                        "summary": { "type": "string", "description": "Event title" },
                        "description": { "type": "string", "description": "Event description" },
                        "location": { "type": "string", "description": "Event location" },
                        "startTime": { "type": "string", "description": "Start time in ISO 8601 format" },
                        "endTime": { "type": "string", "description": "End time in ISO 8601 format" },
                        "attendees": { "type": "array", "items": { "type": "string" }, "description": "List of attendee email addresses" },
                        "timeZone": { "type": "string", "description": "Time zone (default: user's default)" }
                    },
                    "required": ["summary", "startTime", "endTime"]
                }
                """
            },
            new LlmTool
            {
                Name = "calendar_update_event",
                Description = "Update an existing calendar event (requires approval)",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "account": { "type": "string", "description": "Email account to use" },
                        "calendarId": { "type": "string", "description": "Calendar ID" },
                        "eventId": { "type": "string", "description": "Event ID to update" },
                        "summary": { "type": "string", "description": "New event title" },
                        "description": { "type": "string", "description": "New event description" },
                        "location": { "type": "string", "description": "New event location" },
                        "startTime": { "type": "string", "description": "New start time" },
                        "endTime": { "type": "string", "description": "New end time" }
                    },
                    "required": ["eventId"]
                }
                """
            },
            new LlmTool
            {
                Name = "calendar_delete_event",
                Description = "Delete a calendar event (requires approval)",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "account": { "type": "string", "description": "Email account to use" },
                        "calendarId": { "type": "string", "description": "Calendar ID" },
                        "eventId": { "type": "string", "description": "Event ID to delete" }
                    },
                    "required": ["eventId"]
                }
                """
            },
            new LlmTool
            {
                Name = "calendar_list_calendars",
                Description = "List all calendars for an account",
                ParametersSchema = """
                {
                    "type": "object",
                    "properties": {
                        "account": { "type": "string", "description": "Email account to use" }
                    },
                    "required": []
                }
                """
            },
            new LlmTool
            {
                Name = "calendar_list_accounts",
                Description = "List all configured Google Calendar accounts",
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

    protected override async Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct)
    {
        return context.ToolName switch
        {
            "calendar_list_events" => await ListEventsAsync(context.Arguments, ct),
            "calendar_get_event" => await GetEventAsync(context.Arguments, ct),
            "calendar_create_event" => await CreateEventAsync(context.Arguments, ct),
            "calendar_update_event" => await UpdateEventAsync(context.Arguments, ct),
            "calendar_delete_event" => await DeleteEventAsync(context.Arguments, ct),
            "calendar_list_calendars" => await ListCalendarsAsync(context.Arguments, ct),
            "calendar_list_accounts" => ListAccounts(),
            _ => ToolResult.Fail($"Unknown tool: {context.ToolName}")
        };
    }

    private async Task<ToolResult> ListEventsAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<ListEventsArgs>(arguments);
        if (args == null) return ToolResult.Fail("Invalid arguments");

        var service = GetService(args.Account);
        if (service == null) return ToolResult.Fail("No Google Calendar account configured or specified");

        var calendarId = args.CalendarId ?? GetConfig("default_calendar") ?? "primary";
        
        var request = service.Events.List(calendarId);
        request.TimeMinDateTimeOffset = args.TimeMin ?? DateTimeOffset.UtcNow;
        request.TimeMaxDateTimeOffset = args.TimeMax ?? DateTimeOffset.UtcNow.AddDays(7);
        request.MaxResults = args.MaxResults ?? 10;
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

        var events = await request.ExecuteAsync(ct);

        var result = events.Items?.Select(e => new
        {
            id = e.Id,
            summary = e.Summary,
            description = e.Description,
            location = e.Location,
            start = e.Start?.DateTimeDateTimeOffset?.ToString("o") ?? e.Start?.Date,
            end = e.End?.DateTimeDateTimeOffset?.ToString("o") ?? e.End?.Date,
            isAllDay = e.Start?.Date != null,
            attendees = e.Attendees?.Select(a => new { email = a.Email, status = a.ResponseStatus }),
            htmlLink = e.HtmlLink
        }).ToList();

        return ToolResult.Ok(ToJson(new { events = result ?? [], count = result?.Count ?? 0 }));
    }

    private async Task<ToolResult> GetEventAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<GetEventArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.EventId))
            return ToolResult.Fail("Invalid arguments: eventId required");

        var service = GetService(args.Account);
        if (service == null) return ToolResult.Fail("No Google Calendar account configured");

        var calendarId = args.CalendarId ?? GetConfig("default_calendar") ?? "primary";
        var evt = await service.Events.Get(calendarId, args.EventId).ExecuteAsync(ct);

        return ToolResult.Ok(ToJson(new
        {
            id = evt.Id,
            summary = evt.Summary,
            description = evt.Description,
            location = evt.Location,
            start = evt.Start?.DateTimeDateTimeOffset?.ToString("o") ?? evt.Start?.Date,
            end = evt.End?.DateTimeDateTimeOffset?.ToString("o") ?? evt.End?.Date,
            isAllDay = evt.Start?.Date != null,
            organizer = new { email = evt.Organizer?.Email, displayName = evt.Organizer?.DisplayName },
            attendees = evt.Attendees?.Select(a => new 
            { 
                email = a.Email, 
                displayName = a.DisplayName,
                status = a.ResponseStatus,
                optional = a.Optional
            }),
            recurrence = evt.Recurrence,
            conferenceData = evt.ConferenceData != null ? new
            {
                entryPoints = evt.ConferenceData.EntryPoints?.Select(e => new { type = e.EntryPointType, uri = e.Uri })
            } : null,
            htmlLink = evt.HtmlLink
        }));
    }

    private async Task<ToolResult> CreateEventAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<CreateEventArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.Summary))
            return ToolResult.Fail("Invalid arguments: summary required");

        var service = GetService(args.Account);
        if (service == null) return ToolResult.Fail("No Google Calendar account configured");

        var calendarId = args.CalendarId ?? GetConfig("default_calendar") ?? "primary";
        var timeZone = args.TimeZone ?? "UTC";

        var newEvent = new Event
        {
            Summary = args.Summary,
            Description = args.Description,
            Location = args.Location,
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = args.StartTime,
                TimeZone = timeZone
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = args.EndTime,
                TimeZone = timeZone
            }
        };

        if (args.Attendees != null && args.Attendees.Count > 0)
        {
            newEvent.Attendees = args.Attendees.Select(email => new EventAttendee { Email = email }).ToList();
        }

        var created = await service.Events.Insert(newEvent, calendarId).ExecuteAsync(ct);

        return ToolResult.Ok(ToJson(new
        {
            success = true,
            eventId = created.Id,
            htmlLink = created.HtmlLink
        }));
    }

    private async Task<ToolResult> UpdateEventAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<UpdateEventArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.EventId))
            return ToolResult.Fail("Invalid arguments: eventId required");

        var service = GetService(args.Account);
        if (service == null) return ToolResult.Fail("No Google Calendar account configured");

        var calendarId = args.CalendarId ?? GetConfig("default_calendar") ?? "primary";

        // Get existing event
        var existing = await service.Events.Get(calendarId, args.EventId).ExecuteAsync(ct);

        // Apply updates
        if (!string.IsNullOrEmpty(args.Summary)) existing.Summary = args.Summary;
        if (args.Description != null) existing.Description = args.Description;
        if (args.Location != null) existing.Location = args.Location;
        if (args.StartTime.HasValue)
        {
            existing.Start = new EventDateTime { DateTimeDateTimeOffset = args.StartTime };
        }
        if (args.EndTime.HasValue)
        {
            existing.End = new EventDateTime { DateTimeDateTimeOffset = args.EndTime };
        }

        var updated = await service.Events.Update(existing, calendarId, args.EventId).ExecuteAsync(ct);

        return ToolResult.Ok(ToJson(new
        {
            success = true,
            eventId = updated.Id,
            htmlLink = updated.HtmlLink
        }));
    }

    private async Task<ToolResult> DeleteEventAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<DeleteEventArgs>(arguments);
        if (args == null || string.IsNullOrEmpty(args.EventId))
            return ToolResult.Fail("Invalid arguments: eventId required");

        var service = GetService(args.Account);
        if (service == null) return ToolResult.Fail("No Google Calendar account configured");

        var calendarId = args.CalendarId ?? GetConfig("default_calendar") ?? "primary";

        await service.Events.Delete(calendarId, args.EventId).ExecuteAsync(ct);

        return ToolResult.Ok(ToJson(new { success = true, eventId = args.EventId }));
    }

    private async Task<ToolResult> ListCalendarsAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<AccountArgs>(arguments);

        var service = GetService(args?.Account);
        if (service == null) return ToolResult.Fail("No Google Calendar account configured");

        var calendars = await service.CalendarList.List().ExecuteAsync(ct);

        var result = calendars.Items?.Select(c => new
        {
            id = c.Id,
            summary = c.Summary,
            description = c.Description,
            primary = c.Primary,
            accessRole = c.AccessRole,
            backgroundColor = c.BackgroundColor
        }).ToList();

        return ToolResult.Ok(ToJson(new { calendars = result ?? [], count = result?.Count ?? 0 }));
    }

    private ToolResult ListAccounts()
    {
        var accounts = _services.Keys.Select(email => new
        {
            email,
            isDefault = email == GetConfig("default_account")
        }).ToList();

        return ToolResult.Ok(ToJson(new { accounts, count = accounts.Count }));
    }

    private CalendarService? GetService(string? account)
    {
        if (!string.IsNullOrEmpty(account) && _services.TryGetValue(account, out var service))
        {
            return service;
        }

        var defaultAccount = GetConfig("default_account");
        if (!string.IsNullOrEmpty(defaultAccount) && _services.TryGetValue(defaultAccount, out var defaultService))
        {
            return defaultService;
        }

        return _services.Values.FirstOrDefault();
    }

    // Argument classes
    private class AccountArgs
    {
        public string? Account { get; set; }
    }

    private class ListEventsArgs
    {
        public string? Account { get; set; }
        public string? CalendarId { get; set; }
        public DateTimeOffset? TimeMin { get; set; }
        public DateTimeOffset? TimeMax { get; set; }
        public int? MaxResults { get; set; }
    }

    private class GetEventArgs
    {
        public string? Account { get; set; }
        public string? CalendarId { get; set; }
        public string? EventId { get; set; }
    }

    private class CreateEventArgs
    {
        public string? Account { get; set; }
        public string? CalendarId { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public DateTimeOffset? StartTime { get; set; }
        public DateTimeOffset? EndTime { get; set; }
        public List<string>? Attendees { get; set; }
        public string? TimeZone { get; set; }
    }

    private class UpdateEventArgs
    {
        public string? Account { get; set; }
        public string? CalendarId { get; set; }
        public string? EventId { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public DateTimeOffset? StartTime { get; set; }
        public DateTimeOffset? EndTime { get; set; }
    }

    private class DeleteEventArgs
    {
        public string? Account { get; set; }
        public string? CalendarId { get; set; }
        public string? EventId { get; set; }
    }
}

/// <summary>
/// Configuration for a Google Calendar account.
/// </summary>
public class CalendarAccountConfig
{
    public required string Email { get; set; }
    public required string RefreshToken { get; set; }
    public string? AccessToken { get; set; }
}
