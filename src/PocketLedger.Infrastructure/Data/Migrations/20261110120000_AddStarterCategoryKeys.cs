using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PocketLedger.Data;

namespace PocketLedger.Infrastructure.Data.Migrations;

[DbContext(typeof(PocketLedgerDbContext))]
[Migration("20261110120000_AddStarterCategoryKeys")]
public sealed class AddStarterCategoryKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "starter_category_key", table: "categories", type: "text", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "starter_category_key", table: "categories");
    }
}
