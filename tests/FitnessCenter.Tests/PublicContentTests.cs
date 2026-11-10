using FitnessCenter.Web.Data;
using FitnessCenter.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace FitnessCenter.Tests;

public class PublicContentTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task OlderPublishedReviewsRemainReachableAndOnlyFeaturedReviewsAppearOnHome()
    {
        var admin = await database.UserAsync(Roles.Admin);
        var manager = await database.UserAsync(Roles.Manager);
        var session = await database.SessionAsync();
        await using var db = database.CreateDbContext();
        var samples = Enumerable.Range(0, 14).Select(i => new Review
        {
            IsPublished = i != 13,
            Text = $"Отзыв {i}",
            CreatedAtUtc = DateTime.UtcNow.Date.AddMinutes(i),
            Booking = new() { SessionId = session.Id, User = new ApplicationUser { FullName = $"Клиент {i}" }, Status = BookingStatus.Attended }
        }).ToArray();
        db.Reviews.AddRange(samples);
        await db.SaveChangesAsync();

        var staff = new StaffService(database, TimeProvider.System);
        var catalog = new CatalogService(database);
        Assert.Empty(await catalog.FeaturedReviewsAsync());
        Assert.False((await staff.SetReviewFeaturedAsync(admin, samples[13].Id, true)).Success);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.SetReviewFeaturedAsync(manager, samples[0].Id, true));
        Assert.True((await staff.SetReviewFeaturedAsync(admin, samples[0].Id, true)).Success);
        Assert.Equal(samples[0].Id, Assert.Single(await catalog.FeaturedReviewsAsync()).Id);

        var first = await catalog.ReviewsAsync();
        Assert.Equal(12, first.Items.Count);
        Assert.Equal(2, first.TotalPages);
        var older = await catalog.ReviewsAsync(reviewId: samples[0].Id);
        Assert.Equal(2, older.Number);
        Assert.Equal(samples[0].Id, Assert.Single(older.Items).Id);

        Assert.True((await staff.SetReviewPublicationAsync(admin, samples[0].Id, false)).Success);
        Assert.Empty(await catalog.FeaturedReviewsAsync());
    }

    [Fact]
    public async Task AdminChangesInvalidateCachedCatalogAndSiteContent()
    {
        var admin = await database.UserAsync(Roles.Admin);
        var session = await database.SessionAsync();
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new PublicDataCache(memory);
        var catalog = new CatalogService(database, cache);
        var catalogAdmin = new CatalogAdminService(database, cache: cache);
        Assert.Contains(await catalog.ServicesAsync(), s => s.Id == session.ServiceId);
        Assert.True((await catalogAdmin.DeleteServiceAsync(admin, session.ServiceId)).Success);
        Assert.DoesNotContain(await catalog.ServicesAsync(), s => s.Id == session.ServiceId);
        Assert.All(await catalog.TrainersAsync(), trainer => Assert.DoesNotContain(trainer.Services, s => s.Id == session.ServiceId));

        var site = new SiteContentService(database, new ConfigurationBuilder().Build(), cache);
        var before = await site.ReadAsync();
        var section = SiteContentService.Sections[2];
        var edit = section.Fields.ToDictionary(field => field.Key, field => before[field.Key]);
        edit["Home:AccountTitle"] = "Новый заголовок";
        Assert.True((await site.SaveAsync(admin, 2, edit)).Success);
        Assert.Equal("Новый заголовок", (await site.ReadAsync())["Home:AccountTitle"]);
    }
}
