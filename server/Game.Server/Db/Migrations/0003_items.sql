-- T045, T078: what each character owns and where it is, a log of every change to items, and
-- where the character stood when it last left.
alter table characters
    add column position_x double precision null,
    -- Set together with the starter items, so dropping everything never brings them back.
    add column starter_given boolean not null default false;

create table items (
    id bigint generated always as identity primary key,
    character_id bigint not null references characters (id) on delete cascade,
    -- The catalog id from config/items.json, such as gear.kiem. Its place in the catalog can change.
    item_id text not null,
    quantity integer not null default 1 check (quantity > 0),
    -- 1 bag, 2 worn; for a worn item the slot is the gear slot (weapon 0, armour 1, pants 2, hat 3, cape 4).
    place smallint not null check (place in (1, 2)),
    slot smallint not null check (slot >= 0),
    created_at timestamptz not null default now(),
    -- One item per slot. Checked at commit, so two items can swap places inside one transaction.
    constraint items_slot unique (character_id, place, slot) deferrable initially deferred
);

create index items_character on items (character_id);

-- Every change to an item, for looking into lost or duplicated items (GDD 19.3). No foreign keys:
-- the log stays when a character or an item is gone.
create table item_log (
    id bigint generated always as identity primary key,
    character_id bigint not null,
    item_row bigint not null,
    item_id text not null,
    quantity integer not null,
    -- starter, wear, take_off, move, drop, pick_up
    action text not null,
    from_place smallint null,
    from_slot smallint null,
    to_place smallint null,
    to_slot smallint null,
    at timestamptz not null default now()
);

create index item_log_character on item_log (character_id, at);
