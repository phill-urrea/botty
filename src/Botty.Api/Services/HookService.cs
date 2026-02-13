using System.Text.Json;
using Botty.Hooks;
using Botty.Hooks.Models;
using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Botty.Api.Services;

/// <summary>
/// Persists hooks to the database and syncs with the in-memory registry.
/// </summary>
public class HookService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHookRegistry _registry;
    private readonly ILogger<HookService> _logger;

    public HookService(IServiceScopeFactory scopeFactory, IHookRegistry registry, ILogger<HookService> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Loads all enabled hooks from the database and registers them. Call on startup.
    /// </summary>
    public async Task LoadHooksIntoRegistryAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BottyDbContext>();
        var entities = await db.Hooks.Where(h => h.IsEnabled).ToListAsync(ct);
        var scopeFactory = _scopeFactory;
        foreach (var e in entities)
        {
            try
            {
                var hook = CreateDeclarativeHook(scopeFactory, e);
                _registry.Register(hook);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipped loading hook {HookId}: {Name}", e.Id, e.Name);
            }
        }
        _logger.LogInformation("Loaded {Count} hooks into registry", entities.Count);
    }

    public async Task<IEnumerable<HookListDto>> GetAllAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BottyDbContext>();
        return await db.Hooks
            .OrderBy(h => h.Name)
            .Select(h => new HookListDto
            {
                Id = h.Id,
                Name = h.Name,
                Description = h.Description,
                Trigger = h.Trigger,
                ActionType = h.ActionType,
                IsEnabled = h.IsEnabled,
                CreatedAt = h.CreatedAt,
                UpdatedAt = h.UpdatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<HookDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BottyDbContext>();
        var e = await db.Hooks.FindAsync([id], ct);
        if (e == null) return null;
        return new HookDetailDto
        {
            Id = e.Id,
            Name = e.Name,
            Description = e.Description,
            Trigger = e.Trigger,
            ConditionJson = e.ConditionJson,
            ActionType = e.ActionType,
            ActionConfigJson = e.ActionConfigJson,
            IsEnabled = e.IsEnabled,
            CreatedBy = e.CreatedBy,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt
        };
    }

    public async Task<HookDetailDto> CreateAsync(CreateHookRequest request, CancellationToken ct = default)
    {
        var entity = new HookEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Trigger = request.Trigger,
            ConditionJson = request.ConditionJson,
            ActionType = request.ActionType,
            ActionConfigJson = request.ActionConfigJson,
            IsEnabled = request.IsEnabled,
            CreatedBy = request.CreatedBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BottyDbContext>();
            db.Hooks.Add(entity);
            await db.SaveChangesAsync(ct);
        }
        if (entity.IsEnabled)
        {
            var hook = CreateDeclarativeHook(_scopeFactory, entity);
            _registry.Register(hook);
        }
        return new HookDetailDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Trigger = entity.Trigger,
            ConditionJson = entity.ConditionJson,
            ActionType = entity.ActionType,
            ActionConfigJson = entity.ActionConfigJson,
            IsEnabled = entity.IsEnabled,
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public async Task<HookDetailDto?> UpdateAsync(Guid id, UpdateHookRequest request, CancellationToken ct = default)
    {
        HookEntity? entity;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BottyDbContext>();
            entity = await db.Hooks.FindAsync([id], ct);
            if (entity == null) return null;
            entity.Name = request.Name ?? entity.Name;
            entity.Description = request.Description ?? entity.Description;
            entity.Trigger = request.Trigger ?? entity.Trigger;
            if (request.ConditionJson != null) entity.ConditionJson = request.ConditionJson;
            if (request.ActionType != null) entity.ActionType = request.ActionType;
            if (request.ActionConfigJson != null) entity.ActionConfigJson = request.ActionConfigJson;
            if (request.IsEnabled.HasValue) entity.IsEnabled = request.IsEnabled.Value;
            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        _registry.Unregister(id.ToString());
        if (entity!.IsEnabled)
        {
            var hook = CreateDeclarativeHook(_scopeFactory, entity);
            _registry.Register(hook);
        }
        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _registry.Unregister(id.ToString());
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BottyDbContext>();
        var entity = await db.Hooks.FindAsync([id], ct);
        if (entity == null) return false;
        db.Hooks.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IEnumerable<HookExecutionDto>> GetExecutionsAsync(Guid hookId, int limit = 50, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BottyDbContext>();
        return await db.HookExecutions
            .Where(e => e.HookId == hookId)
            .OrderByDescending(e => e.ExecutedAt)
            .Take(limit)
            .Select(e => new HookExecutionDto
            {
                Id = e.Id,
                HookId = e.HookId,
                Trigger = e.Trigger,
                Success = e.Success,
                Output = e.Output,
                Error = e.Error,
                DurationMs = e.DurationMs,
                ExecutedAt = e.ExecutedAt
            })
            .ToListAsync(ct);
    }

    private static DeclarativeHook CreateDeclarativeHook(IServiceScopeFactory scopeFactory, HookEntity e)
    {
        var trigger = Enum.TryParse<HookTrigger>(e.Trigger, true, out var t) ? t : HookTrigger.Custom;
        HookCondition? condition = null;
        if (!string.IsNullOrWhiteSpace(e.ConditionJson))
            condition = JsonSerializer.Deserialize<HookCondition>(e.ConditionJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var config = JsonDocument.Parse(e.ActionConfigJson);
        return new DeclarativeHook(scopeFactory)
        {
            Id = e.Id.ToString(),
            Name = e.Name,
            Description = e.Description,
            Trigger = trigger,
            Condition = condition,
            IsEnabled = e.IsEnabled,
            CreatedBy = e.CreatedBy,
            ActionType = e.ActionType,
            ActionConfig = config
        };
    }
}

public class HookListDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Trigger { get; set; } = "";
    public string ActionType { get; set; } = "";
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class HookDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Trigger { get; set; } = "";
    public string? ConditionJson { get; set; }
    public string ActionType { get; set; } = "";
    public string ActionConfigJson { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateHookRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Trigger { get; set; }
    public string? ConditionJson { get; set; }
    public required string ActionType { get; set; }
    public required string ActionConfigJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? CreatedBy { get; set; }
}

public class UpdateHookRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Trigger { get; set; }
    public string? ConditionJson { get; set; }
    public string? ActionType { get; set; }
    public string? ActionConfigJson { get; set; }
    public bool? IsEnabled { get; set; }
}

public class HookExecutionDto
{
    public Guid Id { get; set; }
    public Guid HookId { get; set; }
    public string Trigger { get; set; } = "";
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public DateTime ExecutedAt { get; set; }
}
