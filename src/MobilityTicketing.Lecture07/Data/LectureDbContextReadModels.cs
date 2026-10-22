using Microsoft.EntityFrameworkCore;

namespace MobilityTicketing.Lecture07.Data;

public partial class LectureDbContext
{
    public DbSet<StoredSnapshot> Snapshots => Set<StoredSnapshot>();
    public DbSet<StoredSnapshotRoute> SnapshotRoutes => Set<StoredSnapshotRoute>();

    partial void ConfigureReadModels(ModelBuilder b)
    {
        b.Entity<StoredSnapshot>(e =>
        {
            e.ToTable("snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FromUtc).HasColumnName("from_utc");
            e.Property(x => x.ToUtc).HasColumnName("to_utc");
            e.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
            e.HasMany(x => x.Routes).WithOne().HasForeignKey(x => x.SnapshotId);
        });
        b.Entity<StoredSnapshotRoute>(e =>
        {
            e.ToTable("snapshot_routes");
            e.HasKey(x => new { x.SnapshotId, x.RouteId });
            e.Property(x => x.SnapshotId).HasColumnName("snapshot_id");
            e.Property(x => x.RouteId).HasColumnName("route_id");
            e.Property(x => x.RouteName).HasColumnName("route_name");
            e.Property(x => x.ScheduledTripCount).HasColumnName("scheduled_trip_count");
            e.Property(x => x.NextDepartureUtc).HasColumnName("next_departure_utc");
        });
    }
}