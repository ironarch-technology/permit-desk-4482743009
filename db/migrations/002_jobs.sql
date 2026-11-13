-- Work queue. Rows are written in the same transaction as the change that
-- caused them, and picked up by the worker with SKIP LOCKED.

CREATE TABLE jobs (
    id              bigserial PRIMARY KEY,
    kind            text NOT NULL,
    payload         text NOT NULL,
    run_at          timestamptz NOT NULL DEFAULT now(),
    attempts        integer NOT NULL DEFAULT 0,
    locked_until    timestamptz,
    last_error      text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    completed_at    timestamptz
);

CREATE INDEX ix_jobs_due ON jobs (run_at) WHERE completed_at IS NULL;
