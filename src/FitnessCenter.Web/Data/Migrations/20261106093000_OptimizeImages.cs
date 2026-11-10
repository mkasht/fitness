using Microsoft.EntityFrameworkCore.Migrations;
namespace FitnessCenter.Web.Data.Migrations;
public partial class OptimizeImages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE Services SET Image = REPLACE(Image, '.png', '.jpg') WHERE Image IN ('/images/club.png','/images/pool.png','/images/trainer-male.png','/images/trainer-female.png')");
        migrationBuilder.Sql("UPDATE Trainers SET Image = REPLACE(Image, '.png', '.jpg') WHERE Image IN ('/images/trainer-male.png','/images/trainer-female.png')");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE Services SET Image = REPLACE(Image, '.jpg', '.png') WHERE Image IN ('/images/club.jpg','/images/pool.jpg','/images/trainer-male.jpg','/images/trainer-female.jpg')");
        migrationBuilder.Sql("UPDATE Trainers SET Image = REPLACE(Image, '.jpg', '.png') WHERE Image IN ('/images/trainer-male.jpg','/images/trainer-female.jpg')");
    }
}
