-- Which map the character stood on when it last left, by the map file's id (map.test, map.house).
-- Null with a saved position_x means the position was saved before there were doors: the test map.
alter table characters
    add column map_id text null;
