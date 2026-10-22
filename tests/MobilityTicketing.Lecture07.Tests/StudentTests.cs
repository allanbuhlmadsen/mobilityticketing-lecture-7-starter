using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using Xunit;

namespace MobilityTicketing.Lecture07.Tests;

[Trait("Category", "Student")]
public class StudentTests : Fixture
{
    // Add four tests, including a failure or concurrency case.
    // Fixture resets the test database before each test.

    // Protects the contract rule "Trim the name". No supplied test uses a padded
    // name, so a missing Trim would leave every other test green.
    [Fact]
    public async Task Rename_stores_the_route_name_without_surrounding_whitespace()
    {
        // Arrange
        const string padded = "   City Ring   ";

        // Act
        var result = await App.RenameRouteAsync("R1", padded);
        var board = await App.ReadOverviewAsync(TimeWindow.Morning);

        // Assert
        Assert.Equal(RenameResult.Renamed, result);
        Assert.Equal("City Ring", board[0].RouteName);
    }

    // Protects cancellation under concurrency. The supplied cases cancel twice in
    // sequence; eight simultaneous cancellations must still change the trip once
    // and report the other seven as retries, never as seven cancellations.
    [Fact]
    public async Task Concurrent_cancellations_change_the_trip_once_and_report_seven_retries()
    {
        // Arrange
        const string tripId = "T102";

        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => App.CancelTripAsync(tripId)));
        await using var db = Db();
        var tripCount = await db.Trips.CountAsync();
        var status = (await db.Trips.SingleAsync(trip => trip.Id == tripId)).Status;

        // Assert
        Assert.Equal(1, results.Count(result => result == CancelResult.Cancelled));
        Assert.Equal(7, results.Count(result => result == CancelResult.AlreadyCancelled));
        Assert.Equal(9, tripCount);
        Assert.Equal("Cancelled", status);
    }

    // Protects recovery after a failed publication. The supplied case checks that
    // nothing was stored; this one checks that the rolled-back attempt released
    // its lock, so the same plan ID can still be published afterwards.
    [Fact]
    public async Task A_rolled_back_publication_does_not_block_the_same_ID_afterwards()
    {
        // Arrange
        var failing = new StudentApplication(Connection, Clock, new ApplicationHooks
        {
            BeforePublishCommit = () => throw new InvalidOperationException("student publication failure")
        });

        // Act
        var error = await Record.ExceptionAsync(
            () => failing.PublishSnapshotAsync("retry-after-failure", TimeWindow.Morning));
        var second = await App.PublishSnapshotAsync("retry-after-failure", TimeWindow.Morning);
        var saved = await App.ReadSnapshotAsync("retry-after-failure");

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(PublishResult.Published, second);
        AssertRows(Baseline, saved.Routes);
    }

    // Protects the choice of booking lock key. Every supplied case retries the
    // same request ID; two different requests for one scheduled trip must both
    // succeed, which fails if the lock is keyed on the trip instead.
    [Fact]
    public async Task Two_requests_can_book_the_same_trip()
    {
        // Arrange
        const string tripId = "T102";

        // Act
        var first = await App.BookTripAsync(tripId, "request-a");
        var second = await App.BookTripAsync(tripId, "request-b");
        await using var db = Db();
        var bookings = await db.Bookings.CountAsync();

        // Assert
        Assert.Equal(BookingStatus.Booked, first.Status);
        Assert.Equal(BookingStatus.Booked, second.Status);
        Assert.NotEqual(first.Receipt, second.Receipt);
        Assert.Equal(2, bookings);
    }
}