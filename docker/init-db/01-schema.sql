-- Enable pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;

-- =============================================================================
-- MEMORY SYSTEM
-- =============================================================================

-- Core memory table
CREATE TABLE memories (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    type VARCHAR(50) NOT NULL,
    content TEXT NOT NULL,
    embedding VECTOR(1536),
    confidence DECIMAL(3,2) DEFAULT 1.0,
    sensitivity VARCHAR(20) DEFAULT 'private',
    source VARCHAR(100),
    supersedes_id UUID REFERENCES memories(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ,
    is_active BOOLEAN DEFAULT true
);

-- Index for vector similarity search
CREATE INDEX idx_memories_embedding ON memories 
    USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

-- Index for filtering active memories by user
CREATE INDEX idx_memories_user_active ON memories(user_id) WHERE is_active = true;

-- Index for expiration cleanup
CREATE INDEX idx_memories_expires ON memories(expires_at) WHERE expires_at IS NOT NULL AND is_active = true;

-- =============================================================================
-- KANBAN WORKFLOW
-- =============================================================================

-- Kanban tasks table
CREATE TABLE kanban_tasks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(255) NOT NULL,
    description TEXT,
    lane VARCHAR(20) NOT NULL DEFAULT 'todo',
    assignee VARCHAR(20) NOT NULL DEFAULT 'assistant',
    task_type VARCHAR(50) NOT NULL DEFAULT 'general',
    priority VARCHAR(20) DEFAULT 'normal',
    pending_action JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ,
    approved_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    rejection_reason TEXT,
    execution_result TEXT
);

-- Indexes for common queries
CREATE INDEX idx_tasks_lane ON kanban_tasks(lane);
CREATE INDEX idx_tasks_assignee ON kanban_tasks(assignee);
CREATE INDEX idx_tasks_lane_assignee ON kanban_tasks(lane, assignee);

-- =============================================================================
-- SCHEDULED TASKS
-- =============================================================================

-- Scheduled tasks table
CREATE TABLE scheduled_tasks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    description TEXT,
    cron_expression VARCHAR(100) NOT NULL,
    next_run_at TIMESTAMPTZ NOT NULL,
    last_run_at TIMESTAMPTZ,
    task_template JSONB NOT NULL,
    assignee VARCHAR(20) DEFAULT 'assistant',
    is_recurring BOOLEAN DEFAULT true,
    max_occurrences INT,
    occurrence_count INT DEFAULT 0,
    is_active BOOLEAN DEFAULT true,
    created_by VARCHAR(20) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Index for finding due tasks
CREATE INDEX idx_scheduled_next_run ON scheduled_tasks(next_run_at) 
    WHERE is_active = true;

-- =============================================================================
-- SKILL CONFIGURATION
-- =============================================================================

-- Skill configuration values (non-sensitive only)
CREATE TABLE skill_configs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    skill_id VARCHAR(100) NOT NULL,
    key VARCHAR(100) NOT NULL,
    value TEXT,
    is_sensitive BOOLEAN NOT NULL DEFAULT false,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(skill_id, key)
);

-- Secret references (tracks what secrets exist, not their values)
CREATE TABLE secret_references (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    category VARCHAR(50) NOT NULL,
    reference_id VARCHAR(100) NOT NULL,
    key VARCHAR(100) NOT NULL,
    secret_path VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(category, reference_id, key)
);

-- =============================================================================
-- SOUL CONFIGURATION
-- =============================================================================

-- Soul configuration versions
CREATE TABLE soul_versions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    content TEXT NOT NULL,
    changed_by VARCHAR(100),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_active BOOLEAN DEFAULT false
);

-- Index for finding active version
CREATE INDEX idx_soul_active ON soul_versions(is_active) WHERE is_active = true;

-- =============================================================================
-- CONVERSATIONS
-- =============================================================================

-- Conversations table
CREATE TABLE conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    title VARCHAR(255),
    source VARCHAR(50) NOT NULL,
    external_id VARCHAR(255),
    store_memories BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Index for user conversations
CREATE INDEX idx_conversations_user ON conversations(user_id);
CREATE INDEX idx_conversations_external ON conversations(source, external_id);

-- Messages table
CREATE TABLE messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
    role VARCHAR(20) NOT NULL,
    content TEXT NOT NULL,
    sender_name VARCHAR(100),
    external_id VARCHAR(255),
    metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Index for conversation messages
CREATE INDEX idx_messages_conversation ON messages(conversation_id, created_at);

-- =============================================================================
-- USERS
-- =============================================================================

-- Users table (basic for now)
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(255) UNIQUE,
    name VARCHAR(255),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =============================================================================
-- HELPER FUNCTIONS
-- =============================================================================

-- Function to update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Triggers for updated_at
CREATE TRIGGER update_memories_updated_at BEFORE UPDATE ON memories
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_kanban_tasks_updated_at BEFORE UPDATE ON kanban_tasks
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_conversations_updated_at BEFORE UPDATE ON conversations
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_skill_configs_updated_at BEFORE UPDATE ON skill_configs
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_users_updated_at BEFORE UPDATE ON users
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- =============================================================================
-- INITIAL DATA
-- =============================================================================

-- Insert default user
INSERT INTO users (id, email, name) VALUES 
    ('00000000-0000-0000-0000-000000000001', 'default@botty.local', 'Default User');
