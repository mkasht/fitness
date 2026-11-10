using FitnessCenter.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Web.Services;

public record ReviewPage(IReadOnlyList<Review> Items, int Number, int TotalPages);

public sealed class CatalogService(IDbContextFactory<ApplicationDbContext> factory, PublicDataCache? cache = null)
{
    private const int ReviewPageSize = 12;
    public Task<List<FitnessService>> ServicesAsync() =>
        cache?.CatalogAsync("services", ReadServicesAsync) ?? ReadServicesAsync();

    public Task<List<Trainer>> TrainersAsync() =>
        cache?.CatalogAsync("trainers", ReadTrainersAsync) ?? ReadTrainersAsync();

    private async Task<List<FitnessService>> ReadServicesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Services.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync();
    }
    private async Task<List<Trainer>> ReadTrainersAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Trainers.AsNoTracking().Include(x => x.Services.Where(s => s.IsActive)).Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync();
    }
    public async Task<FitnessService?> ServiceAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Services.AsNoTracking().Include(x => x.Trainers.Where(t => t.IsActive)).SingleOrDefaultAsync(x => x.Id == id && x.IsActive);
    }
    public async Task<Trainer?> TrainerAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Trainers.AsNoTracking().Include(x => x.Services.Where(s => s.IsActive)).SingleOrDefaultAsync(x => x.Id == id && x.IsActive);
    }
    public async Task<List<TrainingSession>> SessionsAsync(DateTime from, DateTime to, int? serviceId = null, int? trainerId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Sessions.AsNoTracking().Include(x => x.Service).Include(x => x.Trainer)
            .Include(x => x.Bookings.Where(b => b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed))
            .Where(x => !x.IsCancelled && x.Service.IsActive && x.Trainer.IsActive && x.StartsAtUtc >= from && x.StartsAtUtc < to
                && (serviceId == null || x.ServiceId == serviceId) && (trainerId == null || x.TrainerId == trainerId))
            .OrderBy(x => x.StartsAtUtc).ToListAsync();
    }
    public async Task<List<Review>> FeaturedReviewsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await PublishedReviews(db).Where(review => review.IsFeatured).ToListAsync();
    }

    public async Task<ReviewPage> ReviewsAsync(int page = 1, int? reviewId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = PublishedReviews(db);
        var count = await query.CountAsync();
        var pages = Math.Max(1, (count + ReviewPageSize - 1) / ReviewPageSize);
        if (reviewId is not null)
        {
            var target = await query.Where(review => review.Id == reviewId)
                .Select(review => new { review.Id, review.CreatedAtUtc }).SingleOrDefaultAsync();
            if (target is not null)
            {
                var newer = await query.CountAsync(review => review.CreatedAtUtc > target.CreatedAtUtc ||
                    review.CreatedAtUtc == target.CreatedAtUtc && review.Id > target.Id);
                page = newer / ReviewPageSize + 1;
            }
        }
        page = Math.Clamp(page, 1, pages);
        var items = await query.Skip((page - 1) * ReviewPageSize).Take(ReviewPageSize).ToListAsync();
        return new(items, page, pages);
    }

    private static IOrderedQueryable<Review> PublishedReviews(ApplicationDbContext db) =>
        db.Reviews.AsNoTracking().Where(review => review.IsPublished)
            .Include(review => review.Booking).ThenInclude(booking => booking.User)
            .Include(review => review.Booking).ThenInclude(booking => booking.Session).ThenInclude(session => session.Service)
            .OrderByDescending(review => review.CreatedAtUtc).ThenByDescending(review => review.Id);
    public async Task<(int Count, double AverageRating)> ReviewSummaryAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var summary = await db.Reviews.Where(review => review.IsPublished)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), AverageRating = group.Average(review => (double)review.Rating) })
            .SingleOrDefaultAsync();
        return summary is null ? (0, 0) : (summary.Count, summary.AverageRating);
    }
}
