using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FitnessCenter.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261110110000_FeaturedReviews")]
public sealed class AddFeaturedReviews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsFeatured", "Reviews", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.Sql("""
            UPDATE review SET IsFeatured = 1
            FROM Reviews AS review JOIN Bookings AS booking ON booking.Id = review.BookingId
            JOIN AspNetUsers AS author ON author.Id = booking.UserId
            WHERE review.IsPublished = 1 AND (
                booking.UserId LIKE 'pulse-demo-review-%' OR (
                    author.NormalizedEmail = N'CLIENT@PULSE.LOCAL'
                    AND review.Text = N'Понравилось, что тренер сначала объяснил технику и помог подобрать нагрузку. Просторный зал, удобно записываться на занятие.'
                )
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn("IsFeatured", "Reviews");
}
