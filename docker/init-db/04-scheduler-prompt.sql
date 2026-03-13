-- =============================================================================
-- SCHEDULER PROMPT & TIMEZONE SUPPORT
-- =============================================================================

-- Add prompt column to scheduled_tasks for LLM-powered execution
ALTER TABLE scheduled_tasks ADD COLUMN IF NOT EXISTS prompt TEXT;

-- Add timezone column for display/conversion purposes
ALTER TABLE scheduled_tasks ADD COLUMN IF NOT EXISTS timezone VARCHAR(50);
