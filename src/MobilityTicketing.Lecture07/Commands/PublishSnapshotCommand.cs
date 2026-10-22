using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;
using MobilityTicketing.Lecture07.Queries;

namespace MobilityTicketing.Lecture07.Commands;

public static class PublishSnapshotCommand
{
    public static Task<PublishResult> ExecuteAsync(string connection, TimeProvider clock,
        string snapshotId, TimeWindow window, Action? beforeCommit = null, CancellationToken ct = default)
    {
        DatabaseSession.RequireText(snapshotId, nameof(snapshotId));
        window.Validate();
        var publishedAtUtc = StorageTime.Now(clock);

        return DatabaseSession.WriteAsync(connection, "snapshot:" + snapshotId, async db =>
        {
            var existing = await db.Snapshots.FirstOrDefaultAsync(s => s.Id == snapshotId, ct);
            if (existing is not null)
            {
                if (existing.FromUtc != window.FromUtc || existing.ToUtc != window.ToUtc)
                    throw new SnapshotConflictException(snapshotId);
                return PublishResult.AlreadyPublished;
            }

            var rows = await LiveRouteOverviewQuery.ExecuteAsync(db, window, ct);
            db.Snapshots.Add(new StoredSnapshot
            {
                Id = snapshotId,
                FromUtc = window.FromUtc,
                ToUtc = window.ToUtc,
                PublishedAtUtc = publishedAtUtc,
                Routes = rows.Select(row => new StoredSnapshotRoute
                {
                    SnapshotId = snapshotId,
                    RouteId = row.RouteId,
                    RouteName = row.RouteName,
                    ScheduledTripCount = row.ScheduledTripCount,
                    NextDepartureUtc = row.NextDepartureUtc
                }).ToList()
            });
            return PublishResult.Published;
        }, ct, beforeCommit);
    }
}