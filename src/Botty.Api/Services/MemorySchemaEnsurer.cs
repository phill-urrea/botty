using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Botty.Api.Services;

/// <summary>
/// Ensures memory table search/embedding upgrade columns and indexes exist
/// for databases created before the upgrade scripts were introduced.
/// </summary>
public static class MemorySchemaEnsurer
{
    private const string EnsureMemorySql = @"
-- Memory embedding metadata
ALTER TABLE memories ADD COLUMN IF NOT EXISTS embedding_provider VARCHAR(50);
ALTER TABLE memories ADD COLUMN IF NOT EXISTS embedding_model VARCHAR(100);
ALTER TABLE memories ADD COLUMN IF NOT EXISTS embedding_dimensions INTEGER;

-- Ensure embedding supports variable dimensions
ALTER TABLE memories ALTER COLUMN embedding TYPE vector;

-- Full-text search support
ALTER TABLE memories ADD COLUMN IF NOT EXISTS content_tsv tsvector;
UPDATE memories
SET content_tsv = to_tsvector('english', content)
WHERE content_tsv IS NULL;
CREATE INDEX IF NOT EXISTS idx_memories_content_tsv ON memories USING GIN (content_tsv);

-- Keep tsvector in sync
CREATE OR REPLACE FUNCTION update_memories_content_tsv()
RETURNS TRIGGER AS $$
BEGIN
    NEW.content_tsv = to_tsvector('english', NEW.content);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_memories_content_tsv ON memories;
CREATE TRIGGER trg_memories_content_tsv
    BEFORE INSERT OR UPDATE OF content ON memories
    FOR EACH ROW EXECUTE FUNCTION update_memories_content_tsv();
";

    public static async Task EnsureAsync(BottyDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(EnsureMemorySql, ct);
    }
}
