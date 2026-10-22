# Design decision

## Command and query paths

Each method on `StudentApplication` forwards to one command or query class and does nothing else itself. `[tx: ... ]` marks one database transaction, and `|` separates the stages inside it.

    ReadOverviewAsync     -> LiveRouteOverviewQuery    reads routes, trips
                             one statement, no explicit transaction

    CancelTripAsync       -> CancelTripCommand         writes trips
                             [tx: advisory lock trip:<tripId>
                                  | row lock on the trip
                                  | update status ]

    RenameRouteAsync      -> RenameRouteCommand        writes routes
                             [tx: advisory lock route:<routeId>
                                  | update short_name ]

    BookTripAsync         -> BookTripCommand           reads trips, writes bookings
                             [tx: advisory lock booking:<requestId>
                                  | look up the request id in bookings
                                  | row lock on the trip
                                  | insert the receipt ]

    PublishSnapshotAsync  -> PublishSnapshotCommand    reads routes, trips
                                                       writes snapshots, snapshot_routes
                             [tx: advisory lock snapshot:<snapshotId>
                                  | look up the plan id in snapshots
                                  | read the live board
                                  | insert the header and its rows ]

    ReadSnapshotAsync     -> PublishedSnapshotQuery    reads snapshots, snapshot_routes
                             one statement, no explicit transaction

The two reads need no explicit transaction: each is a single statement that writes nothing. The four writes all run inside `DatabaseSession.WriteAsync`, which opens the transaction, takes the advisory lock, runs the work, saves the tracked changes and commits.

## Two designs for the published plan

Published plans can be stored as a header with detail rows, or as one row holding a JSON document. Both answer a read in one query and load no source entities. We chose the header with detail rows. PostgreSQL then enforces the shape: `not null` columns, an integer count, a foreign key from each row to its header, and a composite primary key that prevents the same route appearing twice in one plan. A JSON document moves those checks into our code, and its timestamps are formatted by our serializer rather than stored in `timestamptz` columns, so the database no longer decides their precision. The relational form also stays readable with ordinary SQL, which matters for data someone will query later. The cost is two tables instead of one, a join on every read, and a migration whenever the stored shape changes.

## Transaction boundaries

**Booking.** One transaction covers the request lookup, the trip row lock and the insert. The advisory lock is keyed on the request id, so retries of the same request queue behind each other and the later ones find the first one's receipt instead of inserting a second row. The row lock from `DatabaseSession.LockTripAsync` is keyed on the trip, so a booking and a cancellation of the same trip cannot both read status `Scheduled`. `bookings.request_id` is the primary key, so a duplicate would still be rejected by PostgreSQL if both locks were removed: the locks decide what answer the caller gets, the key decides what the database will hold.

**Publication.** One transaction covers the plan lookup, the live-board read and the insert of the header together with its rows. The live read is inside the transaction on purpose. If it ran outside, a cancellation could commit between reading and storing, and the stored plan would hold values the board never showed. The advisory lock is keyed on the plan id, so simultaneous publications of one id produce one plan and the rest become retries. `ApplicationHooks.BeforePublishCommit` runs after the rows are saved but before the commit, so a failure there rolls back the header and its rows together and leaves no half-written plan.

## What a cancellation updates

`CancelTripAsync` updates one column of one row: `lab07.trips.status` becomes `Cancelled`. The trip row itself is kept, so the departure stays in the record. Nothing else is written: not `routes`, not `bookings`, and no published plan. The live board changes on the next read because it counts only trips with status `Scheduled`, so the cancelled departure drops out of the count, and if it was the earliest one, the next departure moves to the following trip.

## How a published plan keeps its original values

A published plan stores copies, not references. `snapshot_routes` holds the route name, the count and the next departure as they were at publication, and there is deliberately no foreign key from `snapshot_routes.route_id` to `lab07.routes(id)`. Reading a plan therefore touches only `snapshots` and `snapshot_routes` and never reads `routes` or `trips`, which is also why that read loads no source entities. A cancellation or a rename after publication changes the source tables and the live board and leaves every published plan exactly as it was. Later values are captured by publishing a new plan id; republishing an existing id with the same window is treated as a retry that returns `AlreadyPublished` and preserves the original.