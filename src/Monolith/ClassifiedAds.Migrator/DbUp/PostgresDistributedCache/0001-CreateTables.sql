CREATE TABLE IF NOT EXISTS cache_entries (
    id VARCHAR(449) NOT NULL PRIMARY KEY,
    value BYTEA NOT NULL,
    expires_at_time TIMESTAMPTZ NOT NULL,
    sliding_expiration_in_seconds BIGINT NULL,
    absolute_expiration TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_cache_entries_expires_at_time ON cache_entries (expires_at_time);
