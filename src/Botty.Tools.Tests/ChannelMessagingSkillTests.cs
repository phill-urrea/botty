using System.Text.Json;
using Botty.Channels;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Botty.Tools.Tests;

public class ChannelMessagingToolTests
{
    [Fact]
    public async Task GetTools_ExposesChannelMessagingTools()
    {
        var skill = await CreateSkillAsync();
        var toolNames = skill.GetTools().Select(t => t.Name).ToList();

        Assert.Contains("channel_list_conversations", toolNames);
        Assert.Contains("channel_list_recent_senders", toolNames);
        Assert.Contains("channel_send_message", toolNames);
    }

    [Fact]
    public async Task Execute_ChannelListRecentSenders_ReturnsSenderTargets()
    {
        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = Guid.Empty,
            Source = "whatsapp",
            ExternalId = "15550001111@c.us",
            Title = "Alice",
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddMinutes(-5)
        };

        var repository = new FakeConversationRepository
        {
            Conversations = [conversation],
            Feed =
            [
                new Message
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    Conversation = conversation,
                    Role = MessageRole.ThirdParty,
                    Content = "hello",
                    SenderId = "15550001111@c.us",
                    SenderName = "Alice",
                    CreatedAt = now.AddMinutes(-5)
                }
            ]
        };

        var registry = new FakeChannelRegistry();
        var skill = await CreateSkillAsync(registry, repository);

        var result = await skill.ExecuteAsync(new ToolContext
        {
            ToolName = "channel_list_recent_senders",
            Arguments = """{"channel_id":"whatsapp"}"""
        });

        Assert.True(result.Success);
        using var json = JsonDocument.Parse(result.Result!);
        var recipients = json.RootElement.GetProperty("recipients");
        Assert.True(recipients.GetArrayLength() >= 1);
        var first = recipients[0];
        Assert.Equal("15550001111@c.us", first.GetProperty("chatId").GetString());
        Assert.Equal("Alice", first.GetProperty("senderName").GetString());
    }

    [Fact]
    public async Task Execute_ChannelSendMessage_UsesChannelRegistry()
    {
        var registry = new FakeChannelRegistry();
        var skill = await CreateSkillAsync(registry);

        var result = await skill.ExecuteAsync(new ToolContext
        {
            ToolName = "channel_send_message",
            Arguments = """{"channel_id":"whatsapp","chat_id":"15551234567@c.us","message":"Hello"}"""
        });

        Assert.True(result.Success);
        Assert.Equal("whatsapp", registry.LastChannelId);
        Assert.Equal("15551234567@c.us", registry.LastOutbound?.ChatId);
        Assert.Equal("Hello", registry.LastOutbound?.Text);
    }

    private static async Task<ChannelMessagingTool> CreateSkillAsync(
        FakeChannelRegistry? registry = null,
        FakeConversationRepository? repository = null)
    {
        var conversationRepository = repository ?? new FakeConversationRepository();
        var services = new ServiceCollection();
        services.AddScoped<IConversationRepository>(_ => conversationRepository);
        var provider = services.BuildServiceProvider();

        var skill = new ChannelMessagingTool(
            registry ?? new FakeChannelRegistry(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChannelMessagingTool>.Instance);

        await skill.InitializeAsync(new ToolConfiguration
        {
            ToolId = "channel_messaging",
            Values = []
        });

        return skill;
    }

    private sealed class FakeChannelRegistry : IChannelRegistry
    {
        public string? LastChannelId { get; private set; }
        public OutboundMessage? LastOutbound { get; private set; }

        public event EventHandler<ChannelMessageEventArgs>? MessageReceived;

        public void Register(IChannelPlugin plugin)
        {
        }

        public void Unregister(string channelId)
        {
        }

        public IChannelPlugin? GetChannel(string channelId)
        {
            return new FakeChannelPlugin(channelId);
        }

        public IEnumerable<IChannelPlugin> GetAllChannels()
        {
            yield return new FakeChannelPlugin("whatsapp");
        }

        public IEnumerable<IChannelPlugin> GetConnectedChannels()
        {
            yield return new FakeChannelPlugin("whatsapp");
        }

        public Task InitializeAllAsync(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task InitializeChannelAsync(string channelId, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task<ChannelStatus> GetStatusAsync(string channelId, CancellationToken ct = default)
        {
            return Task.FromResult(new ChannelStatus(true, "acct", "acct", DateTime.UtcNow, null));
        }

        public Task<SendResult> SendToChannelAsync(string channelId, OutboundMessage message, CancellationToken ct = default)
        {
            LastChannelId = channelId;
            LastOutbound = message;
            return Task.FromResult(SendResult.Ok("mid-1"));
        }

        private sealed class FakeChannelPlugin : IChannelPlugin
        {
            public FakeChannelPlugin(string id)
            {
                Id = id;
            }

            public string Id { get; }
            public string Label => Id;
            public string Description => "fake";
            public ChannelCapabilities Capabilities { get; } = new();
            public ChannelConfigSchema ConfigSchema { get; } = new() { ChannelId = "whatsapp", Fields = [] };
            public event EventHandler<IncomingMessage>? MessageReceived;
            public event EventHandler<MessageReaction>? ReactionReceived;
            public event EventHandler<ChannelEvent>? EventReceived;
            public Task InitializeAsync(ChannelConfig config, CancellationToken ct = default) => Task.CompletedTask;
            public Task<ChannelStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(new ChannelStatus(true, "acct", "acct", DateTime.UtcNow, null));
            public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task<SendResult> SendTextAsync(OutboundMessage message, CancellationToken ct = default) => Task.FromResult(SendResult.Ok("mid"));
            public Task<SendResult> SendMediaAsync(OutboundMediaMessage message, CancellationToken ct = default) => Task.FromResult(SendResult.Ok("mid"));
            public Task<SendResult> SendReactionAsync(string chatId, string messageId, string emoji, CancellationToken ct = default) => Task.FromResult(SendResult.Ok("mid"));
            public Task<SendResult> SendPollAsync(OutboundPollMessage message, CancellationToken ct = default) => Task.FromResult(SendResult.Ok("mid"));
            public Task SendTypingIndicatorAsync(string chatId, CancellationToken ct = default) => Task.CompletedTask;
        }
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        public IReadOnlyList<Conversation> Conversations { get; set; } = [];
        public IReadOnlyList<Message> Feed { get; set; } = [];

        public Task<Conversation> GetOrCreateAsync(string source, string? externalId, Guid userId = default, string? title = null, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<Message> AppendMessageAsync(Guid conversationId, MessageRole role, string content, string? senderName = null, string? externalId = null, string? senderId = null, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<Conversation>> ListConversationsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Conversations);
        }

        public Task<IReadOnlyList<Message>> GetMergedFeedAsync(DateTime? since = null, int limit = 500, CancellationToken ct = default)
        {
            IEnumerable<Message> query = Feed;
            if (since.HasValue)
            {
                query = query.Where(m => m.CreatedAt > since.Value);
            }

            return Task.FromResult<IReadOnlyList<Message>>(query.Take(limit).ToList());
        }

        public Task<Conversation?> GetByIdAsync(Guid conversationId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId, int limit = 100, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task UpdateMessageContentAsync(Guid messageId, string content, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }
}
