namespace MobilityTicketing.Lecture07.Data;

// PostgreSQL stores timestamptz values with microsecond precision, while .NET
// measures time in ticks of 100 nanoseconds. A value written with a finer
// precision than the column can hold comes back changed, so every timestamp
// this lab stores is cut down to whole microseconds before it is written.
public static class StorageTime
{
    public static DateTime Now(TimeProvider clock)
    {
        var ticks = clock.GetUtcNow().UtcDateTime.Ticks;
        return new DateTime(ticks - ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
    }
}