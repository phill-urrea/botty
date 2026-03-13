using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Botty.Api.Services;

/// <summary>
/// Ensures scheduler prompt and timezone columns exist for older databases.
/// </summary>
public static class SchedulerSchemaEnsurer
{
    private const string EnsureSchedulerSql = @"
ALTER TABLE scheduled_tasks ADD COLUMN IF NOT EXISTS prompt TEXT;
ALTER TABLE scheduled_tasks ADD COLUMN IF NOT EXISTS timezone VARCHAR(50);
";

    public static async Task EnsureAsync(BottyDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(EnsureSchedulerSql, ct);
    }
}
