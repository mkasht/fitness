using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FitnessCenter.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261109120000_DistinctServiceImages")]
public sealed class DistinctServiceImages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE service
        SET Image = images.NewImage
        FROM Services AS service
        INNER JOIN (VALUES
            (N'Функциональный тренинг', N'/images/club.jpg', N'/images/service-functional.webp'),
            (N'Йога', N'/images/trainer-female.jpg', N'/images/service-yoga.webp'),
            (N'Пилатес', N'/images/trainer-female.jpg', N'/images/pulse-studio.webp'),
            (N'Персональная тренировка', N'/images/trainer-male.jpg', N'/images/service-personal.webp')
        ) AS images(Name, OldImage, NewImage) ON images.Name = service.Name
        WHERE service.Image = images.OldImage;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE service
        SET Image = images.OldImage
        FROM Services AS service
        INNER JOIN (VALUES
            (N'Функциональный тренинг', N'/images/club.jpg', N'/images/service-functional.webp'),
            (N'Йога', N'/images/trainer-female.jpg', N'/images/service-yoga.webp'),
            (N'Пилатес', N'/images/trainer-female.jpg', N'/images/pulse-studio.webp'),
            (N'Персональная тренировка', N'/images/trainer-male.jpg', N'/images/service-personal.webp')
        ) AS images(Name, OldImage, NewImage) ON images.Name = service.Name
        WHERE service.Image = images.NewImage;
        """);
}
