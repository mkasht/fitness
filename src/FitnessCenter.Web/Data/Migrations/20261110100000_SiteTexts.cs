using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FitnessCenter.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261110100000_SiteTexts")]
public sealed class AddSiteTexts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "SiteTexts",
            columns: table => new
            {
                Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SiteTexts", x => x.Key));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "SiteTexts");
}
