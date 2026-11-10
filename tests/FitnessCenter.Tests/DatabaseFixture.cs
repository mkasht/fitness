using System.Security.Claims;
using FitnessCenter.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace FitnessCenter.Tests;

public sealed class DatabaseFixture : IDbContextFactory<ApplicationDbContext>, IAsyncLifetime
{
    private readonly string databaseName = "PulseFitnessTests_" + Guid.NewGuid().ToString("N");
    private ServiceProvider services = null!;
    private IDbContextFactory<ApplicationDbContext> factory = null!;
    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlServer(
            $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True"));
        collection.AddIdentityCore<ApplicationUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        services = collection.BuildServiceProvider();
        factory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        foreach (var role in new[] { Roles.Admin, Roles.Manager, Roles.User }) db.Roles.Add(new IdentityRole { Id = role, Name = role, NormalizedName = role.ToUpperInvariant() });
        await db.SaveChangesAsync();
    }
    public ApplicationDbContext CreateDbContext() => factory.CreateDbContext();
    public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    public async Task<ClaimsPrincipal> UserAsync(string role = Roles.User)
    {
        await using var db = CreateDbContext();
        var id = Guid.NewGuid().ToString("N");
        db.Users.Add(new() { Id = id, FullName = "Проверка", UserName = id + "@test.example", Email = id + "@test.example", SecurityStamp = Guid.NewGuid().ToString() });
        db.UserRoles.Add(new() { UserId = id, RoleId = role });
        await db.SaveChangesAsync();
        return new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Role, role)], "Test"));
    }
    public async Task<TrainingSession> SessionAsync(int capacity = 1, DateTime? start = null)
    {
        await using var db = CreateDbContext();
        var service = new FitnessService { Name = "Проверка", Category = "Сила", Summary = "Описание", Description = "Тестовая услуга", DurationMinutes = 60 };
        var trainer = new Trainer { Name = "Тестовый тренер", Specialty = "Сила", Biography = "Тест", Services = [service] };
        var s = new TrainingSession { Service = service, Trainer = trainer, Capacity = capacity, StartsAtUtc = start ?? DateTime.UtcNow.AddDays(2), EndsAtUtc = (start ?? DateTime.UtcNow.AddDays(2)).AddHours(1) };
        db.Sessions.Add(s); await db.SaveChangesAsync(); return s;
    }
    public async Task DisposeAsync()
    {
        // Only the uniquely named disposable test database is removed.
        if (!databaseName.StartsWith("PulseFitnessTests_", StringComparison.Ordinal) || databaseName.Length != 50) throw new InvalidOperationException("Unexpected test database name.");
        await using var db = CreateDbContext(); await db.Database.EnsureDeletedAsync();
        await services.DisposeAsync();
    }
}
