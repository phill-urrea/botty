using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Botty.Api.Services;

/// <summary>
/// Ensures Kanban task context columns exist for older databases.
/// </summary>
public static class KanbanSchemaEnsurer
{
    private const string EnsureKanbanSql = @"
ALTER TABLE kanban_tasks ADD COLUMN IF NOT EXISTS conversation_id UUID;
ALTER TABLE kanban_tasks ADD COLUMN IF NOT EXISTS user_id UUID;
ALTER TABLE kanban_tasks ADD COLUMN IF NOT EXISTS source VARCHAR(50);
ALTER TABLE kanban_tasks ADD COLUMN IF NOT EXISTS external_id VARCHAR(255);
";

    public static async Task EnsureAsync(BottyDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(EnsureKanbanSql, ct);
    }
}
