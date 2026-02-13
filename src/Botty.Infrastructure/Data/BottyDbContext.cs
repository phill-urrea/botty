using Botty.Core.Models;
using Botty.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Botty.Infrastructure.Data;

/// <summary>
/// Entity Framework DbContext for the Botty database.
/// </summary>
public class BottyDbContext : DbContext
{
    public BottyDbContext(DbContextOptions<BottyDbContext> options) : base(options)
    {
    }

    public DbSet<Memory> Memories => Set<Memory>();
    public DbSet<KanbanTask> KanbanTasks => Set<KanbanTask>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<SkillConfigValue> SkillConfigs => Set<SkillConfigValue>();
    public DbSet<SecretReference> SecretReferences => Set<SecretReference>();
    public DbSet<SoulVersion> SoulVersions => Set<SoulVersion>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<User> Users => Set<User>();
    public DbSet<HookEntity> Hooks => Set<HookEntity>();
    public DbSet<HookExecutionEntity> HookExecutions => Set<HookExecutionEntity>();
    public DbSet<EmbeddingCacheEntry> EmbeddingCache => Set<EmbeddingCacheEntry>();
    public DbSet<ChannelPairingRequest> ChannelPairingRequests => Set<ChannelPairingRequest>();
    public DbSet<ChannelAllowListEntry> ChannelAllowList => Set<ChannelAllowListEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Memory entity configuration
        modelBuilder.Entity<Memory>(entity =>
        {
            entity.ToTable("memories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Type).HasColumnName("type").HasConversion<string>();
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.Embedding).HasColumnName("embedding").HasColumnType("vector");
            entity.Property(e => e.Confidence).HasColumnName("confidence");
            entity.Property(e => e.ContentTsv).HasColumnName("content_tsv").HasColumnType("tsvector");
            entity.Property(e => e.EmbeddingProvider).HasColumnName("embedding_provider").HasMaxLength(50);
            entity.Property(e => e.EmbeddingModel).HasColumnName("embedding_model").HasMaxLength(100);
            entity.Property(e => e.EmbeddingDimensions).HasColumnName("embedding_dimensions");
            entity.Property(e => e.Sensitivity).HasColumnName("sensitivity").HasConversion<string>();
            entity.Property(e => e.Source).HasColumnName("source");
            entity.Property(e => e.SupersedesId).HasColumnName("supersedes_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.IsActive).HasColumnName("is_active");

            entity.HasIndex(e => e.UserId);
        });

        // KanbanTask entity configuration
        modelBuilder.Entity<KanbanTask>(entity =>
        {
            entity.ToTable("kanban_tasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Lane).HasColumnName("lane").HasConversion<string>();
            entity.Property(e => e.Assignee).HasColumnName("assignee").HasConversion<string>();
            entity.Property(e => e.Type).HasColumnName("task_type").HasConversion<string>();
            entity.Property(e => e.Priority).HasColumnName("priority").HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.ApprovedAt).HasColumnName("approved_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
            entity.Property(e => e.ExecutionResult).HasColumnName("execution_result");

            // PendingActionData as JSON (Payload stored as string to avoid EF JSON reader NRE with Dictionary)
            entity.OwnsOne(e => e.PendingActionData, pa =>
            {
                pa.Property(p => p.PayloadJson).HasJsonPropertyName("Payload");
                pa.ToJson("pending_action");
            });

            entity.HasIndex(e => e.Lane);
            entity.HasIndex(e => e.Assignee);
        });

        // ScheduledTask entity configuration
        modelBuilder.Entity<ScheduledTask>(entity =>
        {
            entity.ToTable("scheduled_tasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.CronExpression).HasColumnName("cron_expression");
            entity.Property(e => e.NextRunAt).HasColumnName("next_run_at");
            entity.Property(e => e.LastRunAt).HasColumnName("last_run_at");
            entity.Property(e => e.Assignee).HasColumnName("assignee").HasConversion<string>();
            entity.Property(e => e.IsRecurring).HasColumnName("is_recurring");
            entity.Property(e => e.MaxOccurrences).HasColumnName("max_occurrences");
            entity.Property(e => e.OccurrenceCount).HasColumnName("occurrence_count");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            // TaskTemplate as JSON (with nested PendingAction; Payload as string to avoid EF JSON reader NRE)
            entity.OwnsOne(e => e.TaskTemplate, tt =>
            {
                tt.OwnsOne(t => t.PendingAction, pa =>
                {
                    pa.Property(p => p.PayloadJson).HasJsonPropertyName("Payload");
                });
                tt.ToJson("task_template");
            });
        });

        // SkillConfigValue entity configuration
        modelBuilder.Entity<SkillConfigValue>(entity =>
        {
            entity.ToTable("skill_configs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SkillId).HasColumnName("skill_id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.IsSensitive).HasColumnName("is_sensitive");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => new { e.SkillId, e.Key }).IsUnique();
        });

        // SecretReference entity configuration
        modelBuilder.Entity<SecretReference>(entity =>
        {
            entity.ToTable("secret_references");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.SecretPath).HasColumnName("secret_path");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => new { e.Category, e.ReferenceId, e.Key }).IsUnique();
        });

        // SoulVersion entity configuration
        modelBuilder.Entity<SoulVersion>(entity =>
        {
            entity.ToTable("soul_versions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
        });

        // Conversation entity configuration
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Source).HasColumnName("source");
            entity.Property(e => e.ExternalId).HasColumnName("external_id");
            entity.Property(e => e.StoreMemories).HasColumnName("store_memories");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasMany(e => e.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Message entity configuration
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.Role).HasColumnName("role").HasConversion<string>();
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.SenderName).HasColumnName("sender_name");
            entity.Property(e => e.ExternalId).HasColumnName("external_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            // Metadata as JSONB
            entity.Property(e => e.Metadata)
                .HasColumnName("metadata")
                .HasColumnType("jsonb");
        });

        // User entity configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        // Hook entity configuration
        modelBuilder.Entity<HookEntity>(entity =>
        {
            entity.ToTable("hooks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Trigger).HasColumnName("trigger");
            entity.Property(e => e.ConditionJson).HasColumnName("condition").HasColumnType("jsonb");
            entity.Property(e => e.ActionType).HasColumnName("action_type");
            entity.Property(e => e.ActionConfigJson).HasColumnName("action_config").HasColumnType("jsonb");
            entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(e => e.Trigger).HasFilter("is_enabled = true");
        });

        // HookExecution entity configuration
        modelBuilder.Entity<HookExecutionEntity>(entity =>
        {
            entity.ToTable("hook_executions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.HookId).HasColumnName("hook_id");
            entity.Property(e => e.Trigger).HasColumnName("trigger");
            entity.Property(e => e.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(e => e.Success).HasColumnName("success");
            entity.Property(e => e.Output).HasColumnName("output");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.ExecutedAt).HasColumnName("executed_at");
            entity.HasIndex(e => e.HookId);
            entity.HasIndex(e => e.ExecutedAt).IsDescending();
        });

        // EmbeddingCache entity configuration
        modelBuilder.Entity<EmbeddingCacheEntry>(entity =>
        {
            entity.ToTable("embedding_cache");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.TextHash).HasColumnName("text_hash").HasMaxLength(64);
            entity.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(50);
            entity.Property(e => e.Model).HasColumnName("model").HasMaxLength(100);
            entity.Property(e => e.Embedding).HasColumnName("embedding").HasColumnType("vector");
            entity.Property(e => e.Dimensions).HasColumnName("dimensions");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => new { e.TextHash, e.Provider, e.Model }).IsUnique();
        });

        // ChannelPairingRequest entity configuration
        modelBuilder.Entity<ChannelPairingRequest>(entity =>
        {
            entity.ToTable("channel_pairing_requests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(50);
            entity.Property(e => e.SenderId).HasColumnName("sender_id").HasMaxLength(100);
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(8);
            entity.Property(e => e.Meta).HasColumnName("meta").HasColumnType("jsonb");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");

            entity.HasIndex(e => new { e.Channel, e.Code }).IsUnique();
        });

        // ChannelAllowListEntry entity configuration
        modelBuilder.Entity<ChannelAllowListEntry>(entity =>
        {
            entity.ToTable("channel_allow_list");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(50);
            entity.Property(e => e.Entry).HasColumnName("entry").HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => new { e.Channel, e.Entry }).IsUnique();
        });
    }
}

/// <summary>
/// Simple user entity.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
