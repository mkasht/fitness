using FitnessCenter.Web.Data;
using FitnessCenter.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Tests;

public class CatalogImageTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task AdminCanReplaceServiceImageAndInvalidUploadKeepsTheSavedImage()
    {
        var session = await database.SessionAsync();
        var admin = await database.UserAsync(Roles.Admin);
        var manager = await database.UserAsync(Roles.Manager);
        var catalog = new CatalogAdminService(database);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jR6sAAAAASUVORK5CYII=");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => catalog.SaveServiceAsync(manager, session.Service, png));
        Assert.True((await catalog.SaveServiceAsync(admin, session.Service, png)).Success);

        var saved = Assert.IsType<FitnessService>(await new CatalogService(database).ServiceAsync(session.ServiceId));
        var imageId = Guid.Parse(saved.Image.Split('/')[^1]);
        await using var db = database.CreateDbContext();
        var stored = await db.CatalogImages.SingleAsync(image => image.Id == imageId);
        Assert.Equal($"/images/catalog/{imageId:D}", saved.Image);
        Assert.Equal("image/png", stored.ContentType);
        Assert.Equal(png, stored.Content);

        Assert.False((await catalog.SaveServiceAsync(admin, saved, "<svg/>"u8.ToArray())).Success);
        Assert.Equal(saved.Image, await db.Services.Where(s => s.Id == saved.Id).Select(s => s.Image).SingleAsync());
        Assert.Equal(1, await db.CatalogImages.CountAsync());
    }
}
