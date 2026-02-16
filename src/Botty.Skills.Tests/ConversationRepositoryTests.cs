using Botty.Core.Models;
using Botty.Infrastructure.Data;
using Botty.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Botty.Skills.Tests;

public class ConversationRepositoryTests
{
    [Fact]
    public async Task GetMergedFeedAsync_ReturnsNewestWindow_InChronologicalOrder()
    {
        await using var context = CreateContext();
        var conversation = CreateConversation();
        context.Conversations.Add(conversation);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 600; i++)
        {
            context.Messages.Add(new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = MessageRole.User,
                Content = $"msg-{i}",
                CreatedAt = baseTime.AddSeconds(i)
            });
        }

        await context.SaveChangesAsync();

        var repository = new ConversationRepository(context);
        var result = await repository.GetMergedFeedAsync(limit: 500);

        Assert.Equal(500, result.Count);
        Assert.Equal("msg-101", result.First().Content);
        Assert.Equal("msg-600", result.Last().Content);

        // Ensure returned feed remains oldest->newest for UI rendering.
        for (var i = 1; i < result.Count; i++)
        {
            Assert.True(result[i - 1].CreatedAt <= result[i].CreatedAt);
        }
    }

    [Fact]
    public async Task GetMergedFeedAsync_WithSince_FiltersToRecentMessages()
    {
        await using var context = CreateContext();
        var conversation = CreateConversation();
        context.Conversations.Add(conversation);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 20; i++)
        {
            context.Messages.Add(new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = MessageRole.User,
                Content = $"msg-{i}",
                CreatedAt = baseTime.AddSeconds(i)
            });
        }

        await context.SaveChangesAsync();

        var repository = new ConversationRepository(context);
        var since = baseTime.AddSeconds(15);
        var result = await repository.GetMergedFeedAsync(since: since, limit: 500);

        Assert.Equal(5, result.Count);
        Assert.Equal("msg-16", result.First().Content);
        Assert.Equal("msg-20", result.Last().Content);
    }

    [Fact]
    public async Task GetMergedFeedAsync_IncludesSenderId()
    {
        await using var context = CreateContext();
        var conversation = CreateConversation();
        context.Conversations.Add(conversation);
        context.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = MessageRole.ThirdParty,
            Content = "hello from channel",
            SenderId = "15551234567@c.us",
            SenderName = "Alice",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var repository = new ConversationRepository(context);
        var feed = await repository.GetMergedFeedAsync(limit: 10);
        var message = Assert.Single(feed);
        Assert.Equal("15551234567@c.us", message.SenderId);
        Assert.Equal("Alice", message.SenderName);
    }

    private static BottyDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BottyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TestBottyDbContext(options);
    }

    private static Conversation CreateConversation()
    {
        return new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Source = "admin",
            ExternalId = "default",
            StoreMemories = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private sealed class TestBottyDbContext : BottyDbContext
    {
        public TestBottyDbContext(DbContextOptions<BottyDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // InMemory provider cannot map pgvector/tsvector types used in production.
            modelBuilder.Entity<Botty.Core.Models.Memory>().Ignore(m => m.ContentTsv);
            modelBuilder.Entity<Botty.Core.Models.Memory>().Ignore(m => m.Embedding);
            modelBuilder.Entity<EmbeddingCacheEntry>().Ignore(e => e.Embedding);
            modelBuilder.Entity<Message>().Ignore(m => m.Metadata);
        }
    }
}
