using FitnessCenter.Web.Data;
using FitnessCenter.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FitnessCenter.Tests;

public class AdministrationTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task RemovingServiceCancelsUpcomingBookingsAndPreservesHistory()
    {
        var future = await database.SessionAsync();
        var admin = await database.UserAsync(Roles.Admin);
        var manager = await database.UserAsync(Roles.Manager);
        var client = await database.UserAsync();
        var catalog = new CatalogAdminService(database);
        var bookings = new BookingService(database, TimeProvider.System);
        Assert.True((await bookings.BookAsync(client, future.Id, "")).Success);
        await using (var db = database.CreateDbContext())
        {
            db.Sessions.Add(new()
            {
                ServiceId = future.ServiceId, TrainerId = future.TrainerId,
                StartsAtUtc = DateTime.UtcNow.AddDays(-2), EndsAtUtc = DateTime.UtcNow.AddDays(-2).AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => catalog.DeleteServiceAsync(manager, future.ServiceId));
        Assert.True((await catalog.DeleteServiceAsync(admin, future.ServiceId)).Success);
        Assert.Null(await new CatalogService(database).ServiceAsync(future.ServiceId));
        Assert.Equal(BookingStatus.Cancelled, Assert.Single(await bookings.MyBookingsAsync(client)).Status);
        await using var check = database.CreateDbContext();
        Assert.True(await check.Sessions.Where(s => s.Id == future.Id).Select(s => s.IsCancelled).SingleAsync());
        Assert.Single(await check.Sessions.Where(s => s.ServiceId == future.ServiceId && !s.IsCancelled).ToListAsync());
        Assert.True(await check.Notifications.AnyAsync(n => n.UserId == BookingService.UserId(client) && n.Text.Contains("отменил")));
    }

    [Fact]
    public async Task RemovingUnusedServiceDeletesItAndManagerCannotEditSchedule()
    {
        var admin = await database.UserAsync(Roles.Admin);
        var manager = await database.UserAsync(Roles.Manager);
        var catalog = new CatalogAdminService(database);
        await using var db = database.CreateDbContext();
        var unused = new FitnessService { Name = "Удаление", Category = "Сила", Summary = "Описание", Description = "Проверка" };
        db.Services.Add(unused);
        await db.SaveChangesAsync();
        Assert.True((await catalog.DeleteServiceAsync(admin, unused.Id)).Success);
        Assert.False(await db.Services.AnyAsync(s => s.Id == unused.Id));

        var session = await database.SessionAsync();
        var staff = new StaffService(database, TimeProvider.System);
        var newStart = session.StartsAtUtc.AddHours(2);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            staff.SaveSessionAsync(manager, session.Id, session.ServiceId, session.TrainerId, newStart, 2));
        Assert.True((await staff.SaveSessionAsync(admin, session.Id, session.ServiceId, session.TrainerId, newStart, 2)).Success);
        Assert.Equal(newStart, (await staff.UpcomingSessionsAsync(admin)).Single(s => s.Id == session.Id).StartsAtUtc);
    }

    [Fact]
    public async Task OnlyAdminCanAssignRolesAndPublishContactSettings()
    {
        var admin = await database.UserAsync(Roles.Admin);
        var client = await database.UserAsync();
        var manager = await database.UserAsync(Roles.Manager);
        var users = new UserAdminService(database);
        var list = await users.LoadAsync(admin);
        var selected = list.Single(u => u.Id == BookingService.UserId(client)) with { Role = Roles.Manager };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => users.SaveAsync(manager, selected));
        Assert.True((await users.SaveAsync(admin, selected)).Success);
        Assert.Equal(Roles.Manager, (await users.LoadAsync(admin)).Single(u => u.Id == selected.Id).Role);
        var self = list.Single(u => u.Id == BookingService.UserId(admin)) with { Role = Roles.User };
        Assert.False((await users.SaveAsync(admin, self)).Success);

        var site = new SiteContentService(database, new ConfigurationBuilder().Build());
        var contact = new Dictionary<string, string>
        {
            ["Club:City"] = "Ижевск", ["Club:Address"] = "Тестовый адрес", ["Club:Hours"] = "07:00–23:00",
            ["Club:DirectionsUrl"] = "javascript:alert(1)"
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => site.SaveAsync(manager, 0, contact));
        Assert.False((await site.SaveAsync(admin, 0, contact)).Success);
        Assert.NotEqual("Тестовый адрес", (await site.ReadAsync())["Club:Address"]);
        contact["Club:DirectionsUrl"] = "https://example.com/map";
        Assert.True((await site.SaveAsync(admin, 0, contact)).Success);
        Assert.Equal("Тестовый адрес", (await site.ReadAsync())["Club:Address"]);
    }
}
