namespace MobilityTicketing.Lecture07.Data;

public class StoredSnapshotRoute
{
    public string SnapshotId { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string RouteName { get; set; } = "";
    public int ScheduledTripCount { get; set; }
    public DateTime? NextDepartureUtc { get; set; }
}