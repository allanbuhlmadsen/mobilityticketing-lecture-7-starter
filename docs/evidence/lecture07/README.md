# Results

The table keeps the four rows the starter asked for: the supplied lazy baseline and our live board, each at both data sizes. `Executed commands` counts statements the database actually ran, and the two entity columns count `Route` and `Trip` objects EF Core materialised. Every figure in the table is printed by `RunLog` in the output file linked in the right-hand column.

| Data size | Read | Executed commands | Route entities | Trip entities | SQL log |
| --- | --- | ---: | ---: | ---: | --- |
| 4 routes / 9 trips | Baseline | 5 | 4 | 9 | [02_small_fixture.txt](02_small_fixture.txt) |
| 4 routes / 9 trips | Live board | 1 | 0 | 0 | [02_small_fixture.txt](02_small_fixture.txt) |
| 200 routes / 100,000 trips | Baseline | 201 | 200 | 100,000 | [03_scaled_fixture.txt](03_scaled_fixture.txt) |
| 200 routes / 100,000 trips | Live board | 1 | 0 | 0 | [03_scaled_fixture.txt](03_scaled_fixture.txt) |

Reading a published plan was measured in the same way and used 1 executed command, 0 `Route` entities and 0 `Trip` entities, which is the figure the contract requires.

## The two statements behind those numbers

The baseline sends one statement for the routes and then one statement per route for its trips, which is why the count is one plus the number of routes:

```sql
SELECT r.id, r.short_name
FROM lab07.routes AS r
ORDER BY r.id
```

```sql
SELECT t.id, t.scheduled_departure_utc, t.route_id, t.status
FROM lab07.trips AS t
WHERE t.route_id = @__p_0
```

That second statement carries neither the time window nor the status, so every trip on the route is fetched and turned into an object before anything is filtered. At the large size that is 100,000 `Trip` objects to display a one-hour board.

The live board projects straight into `RouteOverviewDto`, so both conditions are evaluated by PostgreSQL and no entity is built at all:

```sql
SELECT r.id, r.short_name, (
    SELECT count(*)::int
    FROM lab07.trips AS t
    WHERE r.id = t.route_id AND t.status = 'Scheduled' AND t.scheduled_departure_utc >= @__window_FromUtc_0 AND t.scheduled_departure_utc < @__window_ToUtc_1), (
    SELECT min(t0.scheduled_departure_utc)
    FROM lab07.trips AS t0
    WHERE r.id = t0.route_id AND t0.status = 'Scheduled' AND t0.scheduled_departure_utc >= @__window_FromUtc_0 AND t0.scheduled_departure_utc < @__window_ToUtc_1)
FROM lab07.routes AS r
ORDER BY r.id
```

The starter supplies the index `trips_route_window` on `(route_id, scheduled_departure_utc) include (status)`, whose columns match the conditions in both subqueries and which carries `status` in the index itself, so both subqueries can in principle be answered without reading the trip rows. We did not run `explain`, so the plan the server actually chose is not recorded here. At the large size the live board returned the same 200 rows as the baseline, which `Large_fixture_keeps_every_route_and_a_bounded_number_of_reads` asserts.

Reading a published plan is one statement over the read model only, with no reference to `routes` or `trips`:

```sql
SELECT t.id, t.from_utc, t.to_utc, t.published_at_utc, s0.route_id, s0.route_name, s0.scheduled_trip_count, s0.next_departure_utc, s0.snapshot_id
FROM (
    SELECT s.id, s.from_utc, s.to_utc, s.published_at_utc
    FROM lab07.snapshots AS s
    WHERE s.id = @__snapshotId_0
    LIMIT 1
) AS t
LEFT JOIN lab07.snapshot_routes AS s0 ON t.id = s0.snapshot_id
ORDER BY t.id, s0.route_id
```

## Cancellation, rename, rejected booking and unchanged plan

The sequence in [04_operations.txt](04_operations.txt) runs all four against the small fixture. It reads the board, publishes the plan `morning`, books one trip twice under a single request id, cancels a trip, renames a route, attempts a booking on the cancelled trip, and then reads the board again, the saved plan `morning`, and a second plan `revised` published after the changes.

**The board before any change.** Four routes, one executed command:

| Route | Name | Count | Next UTC |
| --- | --- | ---: | --- |
| R1 | City Loop | 2 | 06:00 |
| R2 | Campus Line | 2 | 06:10 |
| R3 | Airport Express | 0 | null |
| R4 | Harbour Shuttle | 0 | null |

**Cancellation.** `app cancel T102` returned `Cancelled`. The departure at 06:00 left the count and the next departure moved to 06:20. The trip row itself is kept rather than deleted: the acceptance test `Cancellation_changes_the_live_board_but_preserves_the_trip_and_other_routes` performs the same cancellation and asserts that `lab07.trips` still holds nine rows and that `T102` has status `Cancelled`.

**Rename.** `app rename R1 "City Ring"` returned `Renamed`, and the next read of the board showed the new name.

**The board after both changes.** Only R1 differs, and the read still used one executed command:

| Route | Name | Count | Next UTC |
| --- | --- | ---: | --- |
| R1 | City Ring | 1 | 06:20 |
| R2 | Campus Line | 2 | 06:10 |
| R3 | Airport Express | 0 | null |
| R4 | Harbour Shuttle | 0 | null |

**Rejected booking.** `app book T102 cancelled-1` refused the cancelled trip and exited with code 2:

```json
{
  "Status": "Cancelled",
  "Receipt": null
}
```

**Unchanged published plan.** `app snapshot morning`, read after the cancellation and the rename, still reported the values from publication time, with `City Loop` and a count of 2. Publishing a second plan id recorded the new values instead, which is how later values are captured:

| Plan | R1 name | R1 count | R1 next UTC |
| --- | --- | ---: | --- |
| `morning`, published before the changes | City Loop | 2 | 06:00 |
| `revised`, published after the changes | City Ring | 1 | 06:20 |

**Booking retries.** The two `app book T201 campus-1` calls returned `Booked` and then `AlreadyBooked`, both carrying the identical receipt timestamp `2026-10-22T19:44:21.339504Z`. The retry read the stored receipt rather than making a new decision.

**Microsecond precision.** PostgreSQL stores `timestamptz` to microseconds, while .NET measures time in ticks of 100 nanoseconds, so a timestamp written with a seventh fractional digit comes back changed. `StorageTime.Now` therefore cuts the clock down to whole microseconds before anything is stored, and the first `app book T201 campus-1` call shows the effect: its receipt is built in memory from that cut-down value, never having been to the database, and printed as `2026-10-22T19:44:21.339504Z` with six fractional digits. The retry's receipt is read back from the database and is identical. Had the first receipt carried a seventh digit, the two would have differed. `Retry_returns_the_identical_receipt_with_a_submicrosecond_clock` tests exactly that path: it moves the clock seven ticks past a whole microsecond and requires both receipts to match. The publication times printed by `app snapshot`, `2026-10-22T19:44:17.8408370Z` and `2026-10-22T19:44:45.0932680Z`, are read back from the database, so their trailing zero shows what the column holds rather than what the application sent.

## Tests

All runs are in [01_tests.txt](01_tests.txt).

| Category | Passed | What it covers |
| --- | ---: | --- |
| Starter | 9 | The supplied checks on the fixture, the baseline and window validation |
| Acceptance | 38 | The supplied checks on the contract |
| Student | 4 | Our own cases in [StudentTests.cs](../../../tests/MobilityTicketing.Lecture07.Tests/StudentTests.cs) |

The four own tests each cover a gap the supplied cases leave open:

| Test | What it protects |
| --- | --- |
| `Rename_stores_the_route_name_without_surrounding_whitespace` | The contract rule "Trim the name". No supplied case uses a padded name, so a missing `Trim` would leave every other test green. |
| `Concurrent_cancellations_change_the_trip_once_and_report_seven_retries` | Cancellation under concurrency. The supplied cases cancel twice in sequence; eight simultaneous cancellations must still change the trip once and report seven retries. |
| `A_rolled_back_publication_does_not_block_the_same_ID_afterwards` | Recovery after a failure. The supplied case checks that nothing was stored; this one checks that the rolled-back attempt released its lock, so the same plan id can still be published. |
| `Two_requests_can_book_the_same_trip` | The choice of booking lock key. Every supplied case retries one request id; two different requests for one scheduled trip must both succeed, which fails if the lock is keyed on the trip. |

## Output files

| File | Contents |
| --- | --- |
| [01_tests.txt](01_tests.txt) | The Starter, Acceptance and Student test runs |
| [02_small_fixture.txt](02_small_fixture.txt) | `reset`, `baseline`, `overview`, `publish morning` and `snapshot morning` on 4 routes / 9 trips |
| [03_scaled_fixture.txt](03_scaled_fixture.txt) | `reset`, `scale`, `baseline` and `overview` on 200 routes / 100,000 trips |
| [04_operations.txt](04_operations.txt) | The cancellation, rename, rejected booking and unchanged plan sequence |

The design note is in [docs/design.md](../../design.md) and the read-model schema in [03-read-model.sql](../../../database/postgres/lecture07/03-read-model.sql).

## Reproducing this run

The starter contains no `.gitattributes`, and `database/postgres/lecture07/00-create-test-db.sh` is a shell script that the PostgreSQL container runs when its data directory is first initialised. On a fresh clone, `git ls-files --eol` reported `w/crlf` for that file: Git for Windows had converted it to CRLF line endings on checkout, which is its default. A `#!/bin/sh` line followed by a carriage return is not a path Linux can resolve, so this fork adds a `.gitattributes` with `*.sh text eol=lf` and checked the script out again before the container was first started. The failure was prevented rather than observed.