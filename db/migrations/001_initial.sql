CREATE TABLE users (
    id              serial PRIMARY KEY,
    email           text NOT NULL UNIQUE,
    password_hash   text NOT NULL,
    full_name       text NOT NULL,
    phone           text,
    role            text NOT NULL DEFAULT 'applicant',
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE parcels (
    id              serial PRIMARY KEY,
    parcel_number   text NOT NULL UNIQUE,
    address         text NOT NULL,
    district        text NOT NULL,
    zoning_code     text NOT NULL
);

CREATE INDEX ix_parcels_address ON parcels (lower(address));

CREATE TABLE work_types (
    code                text PRIMARY KEY,
    label               text NOT NULL,
    min_valuation       double precision NOT NULL,
    max_valuation       double precision NOT NULL,
    sort_order          integer NOT NULL DEFAULT 0
);

CREATE TABLE applications (
    id                          serial PRIMARY KEY,
    reference                   text NOT NULL UNIQUE,
    applicant_user_id           integer NOT NULL REFERENCES users (id),
    state                       text NOT NULL DEFAULT 'draft',
    parcel_id                   integer REFERENCES parcels (id),
    parcel_address              text,
    work_type                   text,
    scope_of_work               text,
    declared_valuation          double precision,
    applicant_type              text NOT NULL DEFAULT 'resident',
    contact_name                text,
    contact_email               text,
    contact_phone               text,
    contractor_licence_number   text,
    contractor_licence_expiry   date,
    submission_key              text,
    correction_cycle            integer NOT NULL DEFAULT 0,
    submitted_at                timestamptz,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_applications_applicant ON applications (applicant_user_id);
CREATE INDEX ix_applications_state ON applications (state);
CREATE UNIQUE INDEX ux_applications_submission_key
    ON applications (applicant_user_id, submission_key)
    WHERE submission_key IS NOT NULL;

CREATE SEQUENCE application_reference_seq START 2400;

CREATE TABLE transitions (
    id              bigserial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    from_state      text,
    to_state        text NOT NULL,
    actor           text NOT NULL,
    source          text NOT NULL,
    reason          text,
    occurred_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_transitions_application ON transitions (application_id, occurred_at);

CREATE TABLE zoning_checks (
    id              serial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    handle          text NOT NULL UNIQUE,
    status          text NOT NULL DEFAULT 'pending',
    reason          text,
    requested_at    timestamptz NOT NULL DEFAULT now(),
    resolved_at     timestamptz
);

CREATE INDEX ix_zoning_checks_pending ON zoning_checks (requested_at) WHERE status = 'pending';

CREATE TABLE review_submissions (
    id              serial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    cycle           integer NOT NULL,
    external_id     text NOT NULL,
    outcome         text,
    submitted_at    timestamptz NOT NULL DEFAULT now(),
    decided_at      timestamptz
);

CREATE TABLE correction_items (
    id              serial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    cycle           integer NOT NULL,
    code            text NOT NULL,
    narrative       text NOT NULL,
    citation        text NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE correction_responses (
    id              serial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    item_code       text NOT NULL,
    body            text NOT NULL,
    responded_at    timestamptz NOT NULL DEFAULT now(),
    UNIQUE (application_id, item_code)
);

CREATE TABLE fee_quotes (
    id              serial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    external_id     text NOT NULL,
    amount          double precision NOT NULL,
    breakdown       text NOT NULL,
    expires_at      timestamptz NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE payments (
    id              serial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    fee_quote_id    integer NOT NULL REFERENCES fee_quotes (id),
    external_id     text NOT NULL,
    amount          double precision NOT NULL,
    status          text NOT NULL DEFAULT 'pending',
    captured_at     timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_payments_external ON payments (external_id);

CREATE SEQUENCE permit_number_seq START 4100;

CREATE TABLE permits (
    id              serial PRIMARY KEY,
    application_id  integer NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
    permit_number   text NOT NULL UNIQUE,
    issued_at       timestamptz NOT NULL DEFAULT now(),
    valid_until     timestamptz NOT NULL
);

CREATE INDEX ix_permits_application ON permits (application_id);
