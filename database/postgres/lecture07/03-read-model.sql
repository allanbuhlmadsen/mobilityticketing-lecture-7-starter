-- Design persistent storage for published dispatch plans in lab07.
-- Include the plan identity, time window, publication time and displayed values.
-- The contract defines behavior; the table layout is your choice.

create table lab07.snapshots (
    id text primary key,
    from_utc timestamptz not null,
    to_utc timestamptz not null,
    published_at_utc timestamptz not null,
    constraint snapshots_window_positive check (to_utc > from_utc)
);

create table lab07.snapshot_routes (
    snapshot_id text not null references lab07.snapshots(id) on delete cascade,
    route_id text not null,
    route_name text not null,
    scheduled_trip_count integer not null,
    next_departure_utc timestamptz,
    constraint snapshot_routes_pk primary key (snapshot_id, route_id),
    constraint snapshot_routes_count_not_negative check (scheduled_trip_count >= 0)
);