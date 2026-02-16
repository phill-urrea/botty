using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Botty.Api.Services;

/// <summary>
/// Ensures conversation/message schema additions exist for older databases.
/// </summary>
public static class ConversationSchemaEnsurer
{
    private const string EnsureConversationSql = @"
ALTER TABLE messages ADD COLUMN IF NOT EXISTS sender_id VARCHAR(255);
";

    public static async Task EnsureAsync(BottyDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(EnsureConversationSql, ct);
    }
}
