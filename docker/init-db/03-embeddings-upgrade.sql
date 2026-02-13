-- =============================================================================
-- PHASE 1: Embedding Provider Support & Variable Dimensions
-- =============================================================================

-- Add embedding metadata columns to memories
ALTER TABLE memories ADD COLUMN IF NOT EXISTS embedding_provider VARCHAR(50);
ALTER TABLE memories ADD COLUMN IF NOT EXISTS embedding_model VARCHAR(100);
ALTER TABLE memories ADD COLUMN IF NOT EXISTS embedding_dimensions INTEGER;

-- Change embedding column from fixed vector(1536) to untyped vector
-- pgvector supports untyped vector columns that accept any dimensionality
ALTER TABLE memories ALTER COLUMN embedding TYPE vector;

-- Drop the old IVFFlat index (requires fixed dimensions)
DROP INDEX IF EXISTS idx_memories_embedding;

-- Create HNSW index (supports untyped vector columns, better performance)
CREATE INDEX IF NOT EXISTS idx_memories_embedding_hnsw ON memories
    USING hnsw (embedding vector_cosine_ops);

-- =============================================================================
-- Embedding Cache Table
-- =============================================================================

CREATE TABLE IF NOT EXISTS embedding_cache (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    text_hash VARCHAR(64) NOT NULL,
    provider VARCHAR(50) NOT NULL,
    model VARCHAR(100) NOT NULL,
    embedding vector,
    dimensions INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_embedding_cache_lookup
    ON embedding_cache(text_hash, provider, model);

-- =============================================================================
-- PHASE 2: Full-Text Search Support
-- =============================================================================

-- Add tsvector column for PostgreSQL full-text search
ALTER TABLE memories ADD COLUMN IF NOT EXISTS content_tsv tsvector;

-- Populate tsvector for existing rows
UPDATE memories SET content_tsv = to_tsvector('english', content)
    WHERE content_tsv IS NULL;

-- GIN index for fast full-text search
CREATE INDEX IF NOT EXISTS idx_memories_content_tsv ON memories USING GIN (content_tsv);

-- Auto-update trigger: keep content_tsv in sync with content
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

-- =============================================================================
-- PHASE 4: Channel Pairing & Allow List Tables
-- =============================================================================

CREATE TABLE IF NOT EXISTS channel_pairing_requests (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    channel VARCHAR(50) NOT NULL,
    sender_id VARCHAR(100) NOT NULL,
    code VARCHAR(8) NOT NULL,
    meta JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_pairing_channel_code
    ON channel_pairing_requests(channel, code);

CREATE TABLE IF NOT EXISTS channel_allow_list (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    channel VARCHAR(50) NOT NULL,
    entry VARCHAR(100) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_allow_list_channel_entry
    ON channel_allow_list(channel, entry);
