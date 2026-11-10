using System.Security.Claims;
using FitnessCenter.Web.Data;
using FitnessCenter.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Tests;

public class BookingTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private BookingService Service => new(database, TimeProvider.System);

    [Fact]
    public async Task ConcurrentRequestsCannotOverbookLastPlace()
    {
        var slot = await database.SessionAsync();
        var a = await database.UserAsync(); var b = await database.UserAsync();
        var results = await Task.WhenAll(Service.BookAsync(a, slot.Id, ""), Service.BookAsync(b, slot.Id, ""));
        Assert.Single(results, x => x.Success);
        await using var db = database.CreateDbContext();
        Assert.Equal(1, await db.Bookings.CountAsync(x => x.SessionId == slot.Id && x.Status == BookingStatus.Pending));
    }

    [Fact]
    public async Task DuplicateSubmissionDoesNotCreateSecondBooking()
    {
        var slot = await database.SessionAsync(8); var user = await database.UserAsync();
        Assert.True((await Service.BookAsync(user, slot.Id, "")).Success);
        Assert.False((await Service.BookAsync(user, slot.Id, "")).Success);
        await using var db = database.CreateDbContext();
        Assert.Equal(1, await db.Bookings.CountAsync(x => x.SessionId == slot.Id));
    }

    [Fact]
    public async Task CancellingReleasesPlaceAndAllowsBookingAgain()
    {
        var slot = await database.SessionAsync(); var a = await database.UserAsync(); var b = await database.UserAsync();
        Assert.True((await Service.BookAsync(a, slot.Id, "")).Success);
        var booking = (await Service.MyBookingsAsync(a)).Single();
        Assert.False((await Service.BookAsync(b, slot.Id, "")).Success);
        Assert.True((await Service.CancelAsync(a, booking.Id)).Success);
        Assert.True((await Service.BookAsync(a, slot.Id, "Повторная запись")).Success);
        Assert.Single(await Service.MyBookingsAsync(a));
    }

    [Fact]
    public async Task UserCannotCancelAnotherUsersBooking()
    {
        var slot = await database.SessionAsync(); var a = await database.UserAsync(); var b = await database.UserAsync();
        Assert.True((await Service.BookAsync(a, slot.Id, "")).Success);
        var booking = (await Service.MyBookingsAsync(a)).Single();
        Assert.False((await Service.CancelAsync(b, booking.Id)).Success);
        Assert.Empty(await Service.MyBookingsAsync(b));
        Assert.Equal(BookingStatus.Pending, (await Service.MyBookingsAsync(a)).Single().Status);
    }

    [Fact]
    public async Task PastAndCancelledSessionsCannotBeBooked()
    {
        var user = await database.UserAsync();
        var past = await database.SessionAsync(start: DateTime.UtcNow.AddDays(-2));
        Assert.False((await Service.BookAsync(user, past.Id, "")).Success);
        var cancelled = await database.SessionAsync();
        await using (var db = database.CreateDbContext()) { await db.Sessions.Where(s => s.Id == cancelled.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsCancelled, true)); }
        Assert.False((await Service.BookAsync(user, cancelled.Id, "")).Success);
    }

    [Fact]
    public async Task OverlappingBookingsAndTrainerSessionsAreRejected()
    {
        var slot = await database.SessionAsync(8); var user = await database.UserAsync();
        Assert.True((await Service.BookAsync(user, slot.Id, "")).Success);
        var overlapping = await database.SessionAsync(8, slot.StartsAtUtc.AddMinutes(15));
        Assert.False((await Service.BookAsync(user, overlapping.Id, "")).Success);
        var admin = await database.UserAsync(Roles.Admin); var staff = new StaffService(database, TimeProvider.System);
        Assert.False((await staff.AddSessionAsync(admin, slot.ServiceId, slot.TrainerId, slot.StartsAtUtc.AddMinutes(15), 8)).Success);
    }

    [Fact]
    public async Task AnonymousUsersAndManagersCannotPerformRestrictedOperations()
    {
        var slot = await database.SessionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.BookAsync(new ClaimsPrincipal(), slot.Id, ""));
        var manager = await database.UserAsync(Roles.Manager);
        var staff = new StaffService(database, TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.AddSessionAsync(manager, slot.ServiceId, slot.TrainerId, slot.EndsAtUtc.AddHours(1), 8));
        var user = await database.UserAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.BookingsAsync(user));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new CatalogAdminService(database).LoadAsync(manager));
    }

    [Fact]
    public async Task AttendanceRequiresConfirmedFinishedSessionAndReviewRequiresAttendance()
    {
        var slot = await database.SessionAsync(); var user = await database.UserAsync(); var manager = await database.UserAsync(Roles.Manager);
        Assert.True((await Service.BookAsync(user, slot.Id, "")).Success);
        var booking = (await Service.MyBookingsAsync(user)).Single();
        var clock = new MutableClock(DateTimeOffset.UtcNow); var staff = new StaffService(database, clock);
        Assert.False((await staff.SetStatusAsync(manager, booking.Id, BookingStatus.Attended)).Success);
        Assert.False((await Service.AddReviewAsync(user, booking.Id, 5, "Хорошая тренировка")).Success);
        Assert.True((await staff.SetStatusAsync(manager, booking.Id, BookingStatus.Confirmed)).Success);
        Assert.False((await staff.SetStatusAsync(manager, booking.Id, BookingStatus.Attended)).Success);
        clock.Now = new DateTimeOffset(DateTime.SpecifyKind(slot.EndsAtUtc.AddMinutes(1), DateTimeKind.Utc));
        Assert.True((await staff.SetStatusAsync(manager, booking.Id, BookingStatus.Attended)).Success);
        Assert.True((await Service.AddReviewAsync(user, booking.Id, 5, "Хорошая тренировка")).Success);
        Assert.False((await Service.AddReviewAsync(user, booking.Id, 4, "Повторный отзыв")).Success);
        Assert.DoesNotContain((await new CatalogService(database).ReviewsAsync()).Items, r => r.BookingId == booking.Id);
    }

    [Fact]
    public async Task CancellingSessionCancelsBookingsAndNotifiesParticipants()
    {
        var slot = await database.SessionAsync(); var user = await database.UserAsync(); var admin = await database.UserAsync(Roles.Admin);
        Assert.True((await Service.BookAsync(user, slot.Id, "")).Success);
        Assert.True((await new StaffService(database, TimeProvider.System).CancelSessionAsync(admin, slot.Id)).Success);
        Assert.Equal(BookingStatus.Cancelled, (await Service.MyBookingsAsync(user)).Single().Status);
        Assert.False((await Service.BookAsync(user, slot.Id, "")).Success);
        await using var db = database.CreateDbContext();
        var uid = BookingService.UserId(user);
        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == uid && n.Text.Contains("отменил")));
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
