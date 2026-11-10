using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<FitnessService> Services => Set<FitnessService>();
    public DbSet<CatalogImage> CatalogImages => Set<CatalogImage>();
    public DbSet<SiteText> SiteTexts => Set<SiteText>();
    public DbSet<Trainer> Trainers => Set<Trainer>();
    public DbSet<TrainingSession> Sessions => Set<TrainingSession>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<FeedbackMessage> Feedback => Set<FeedbackMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().Property(x => x.FullName).HasMaxLength(100);
        builder.Entity<FitnessService>().Property(x => x.Price).HasPrecision(10, 2);
        builder.Entity<TrainingSession>().HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TrainingSession>().HasOne(x => x.Trainer).WithMany().HasForeignKey(x => x.TrainerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TrainingSession>().HasIndex(x => new { x.TrainerId, x.StartsAtUtc });
        builder.Entity<TrainingSession>().ToTable(t => t.HasCheckConstraint("CK_Session_TimeCapacity", "[EndsAtUtc] > [StartsAtUtc] AND [Capacity] > 0"));
        builder.Entity<Booking>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Booking>().HasOne(x => x.Session).WithMany(x => x.Bookings).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Booking>().HasIndex(x => new { x.UserId, x.SessionId }).IsUnique();
        builder.Entity<Booking>().HasIndex(x => new { x.SessionId, x.Status });
        builder.Entity<Review>().HasIndex(x => x.BookingId).IsUnique();
        builder.Entity<Review>().HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Notification>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
