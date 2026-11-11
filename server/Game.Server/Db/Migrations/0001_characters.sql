-- T041: one row per character. Only identity for now; gear, position and stats come with T045,
-- the owning account with T042.
create table characters (
    id bigint generated always as identity primary key,
    display_name text null check (display_name is null or length(display_name) between 1 and 32),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    -- Raised by every save; a save that read an older revision is refused, so two servers
    -- or two retries cannot overwrite each other.
    revision integer not null default 0
);
