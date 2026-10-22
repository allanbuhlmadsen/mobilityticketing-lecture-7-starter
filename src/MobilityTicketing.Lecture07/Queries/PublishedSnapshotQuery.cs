using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Queries;

public static class PublishedSnapshotQuery
{
    public static async Task<PublishedSnapshot> ExecuteAsync(
        LectureDbContext db, string snapshotId, CancellationToken ct = default)
    {
        DatabaseSession.RequireText(snapshotId, nameof(snapshotId));

        var stored = await db.Snapshots
            .AsSingleQuery()
            .Where(snapshot => snapshot.Id == snapshotId)
            .Select(snapshot => new
            {
                snapshot.Id,
                snapshot.FromUtc,
                snapshot.ToUtc,
                snapshot.PublishedAtUtc,
                Routes = snapshot.Routes
                    .OrderBy(route => route.RouteId)
                    .Select(route => new RouteOverviewDto(
                        route.RouteId, route.RouteName,
                        route.ScheduledTripCount, route.NextDepartureUtc))
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (stored is null)
            throw new SnapshotNotFoundException(snapshotId);

        return new PublishedSnapshot(stored.Id,
            new TimeWindow(stored.FromUtc, stored.ToUtc), stored.PublishedAtUtc, stored.Routes);
    }
}