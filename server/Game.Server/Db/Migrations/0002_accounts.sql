-- T042, T082: accounts, login sessions and verification codes; each account has one character per game server.
create table accounts (
    id bigint generated always as identity primary key,
    -- Stored in lower case; the server checks the rules before it gets here.
    username text not null,
    -- "pbkdf2-sha256$iterations$salt$hash"; never the password itself.
    password_hash text not null,
    kind text not null check (kind in ('guest', 'player')),
    -- Email in lower case or a phone as +84 and nine digits, always verified before it is stored.
    contact text null,
    -- SHA-256 of the secret only the guest's device holds; null once the guest is linked.
    guest_secret_hash bytea null,
    failed_logins integer not null default 0,
    locked_until timestamptz null,
    created_at timestamptz not null default now()
);

create unique index accounts_username on accounts (username);
create unique index accounts_contact on accounts (contact) where contact is not null;
create unique index accounts_guest_secret on accounts (guest_secret_hash) where guest_secret_hash is not null;

create table sessions (
    -- SHA-256 of the token the client holds, so a leaked table cannot be used to log in.
    token_hash bytea primary key,
    account_id bigint not null references accounts (id) on delete cascade,
    created_at timestamptz not null default now(),
    expires_at timestamptz not null
);

create index sessions_account on sessions (account_id);

create table verification_codes (
    purpose text not null,
    contact text not null,
    code_hash bytea not null,
    attempts integer not null default 0,
    sent_at timestamptz not null,
    expires_at timestamptz not null,
    primary key (purpose, contact)
);

alter table characters
    add column account_id bigint null references accounts (id) on delete cascade,
    add column server_number integer null,
    -- The name in lower case, worked out by the server, so uniqueness does not depend on the database's locale.
    add column name_key text null;

create unique index characters_account_server on characters (account_id, server_number) where account_id is not null;
create unique index characters_name_server on characters (server_number, name_key) where name_key is not null;
