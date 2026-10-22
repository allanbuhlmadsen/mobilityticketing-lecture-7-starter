using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Commands;

public static class BookTripCommand
{
    public static Task<BookingResult> ExecuteAsync(string connection, TimeProvider clock,
        string tripId, string requestId, CancellationToken ct = default)
    {
        DatabaseSession.RequireText(tripId, nameof(tripId));
        DatabaseSession.RequireText(requestId, nameof(requestId));
        var bookedAtUtc = StorageTime.Now(clock);

        return DatabaseSession.WriteAsync(connection, "booking:" + requestId, async db =>
        {
            var existing = await db.Bookings.FirstOrDefaultAsync(b => b.RequestId == requestId, ct);
            if (existing is not null)
                return existing.TripId == tripId
                    ? new BookingResult(BookingStatus.AlreadyBooked,
                        new BookingReceipt(existing.RequestId, existing.TripId, existing.BookedAtUtc))
                    : new BookingResult(BookingStatus.RequestConflict);

            var trip = await DatabaseSession.LockTripAsync(db, tripId, ct);
            if (trip is null)
                return new BookingResult(BookingStatus.NotFound);
            if (trip.Status == "Cancelled")
                return new BookingResult(BookingStatus.Cancelled);

            db.Bookings.Add(new Booking
            {
                RequestId = requestId,
                TripId = tripId,
                BookedAtUtc = bookedAtUtc
            });
            return new BookingResult(BookingStatus.Booked,
                new BookingReceipt(requestId, tripId, bookedAtUtc));
        }, ct);
    }
}