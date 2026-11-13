CREATE TABLE inspections (
    id              serial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    inspection_type text NOT NULL,
    slot_id         text NOT NULL,
    slot_start      timestamptz NOT NULL,
    external_id     text NOT NULL,
    status          text NOT NULL DEFAULT 'pending',
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_inspections_application ON inspections (application_id);

CREATE TABLE notifications (
    id              serial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    recipient       text NOT NULL,
    template        text NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    sent_at         timestamptz
);
