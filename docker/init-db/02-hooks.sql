-- =============================================================================
-- HOOKS (Phase 11: Webhooks and Event Hooks)
-- =============================================================================

CREATE TABLE IF NOT EXISTS hooks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    description TEXT,
    trigger VARCHAR(50) NOT NULL,
    condition JSONB,
    action_type VARCHAR(50) NOT NULL,
    action_config JSONB NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT true,
    created_by VARCHAR(100),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_hooks_trigger ON hooks(trigger) WHERE is_enabled = true;

CREATE TABLE IF NOT EXISTS hook_executions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    hook_id UUID NOT NULL REFERENCES hooks(id) ON DELETE CASCADE,
    trigger VARCHAR(50) NOT NULL,
    payload JSONB,
    success BOOLEAN NOT NULL,
    output TEXT,
    error TEXT,
    duration_ms INT NOT NULL,
    executed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_hook_executions_hook_id ON hook_executions(hook_id);
CREATE INDEX IF NOT EXISTS idx_hook_executions_executed_at ON hook_executions(executed_at DESC);

CREATE TRIGGER update_hooks_updated_at BEFORE UPDATE ON hooks
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
