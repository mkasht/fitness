using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FitnessCenter.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261110090000_CatalogImages")]
public sealed class AddCatalogImages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "CatalogImages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ContentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_CatalogImages", x => x.Id));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "CatalogImages");
}
