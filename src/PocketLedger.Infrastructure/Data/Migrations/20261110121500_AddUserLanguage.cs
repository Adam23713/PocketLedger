using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PocketLedger.Data;

namespace PocketLedger.Infrastructure.Data.Migrations;

[DbContext(typeof(PocketLedgerDbContext))]
[Migration("20261110121500_AddUserLanguage")]
public sealed class AddUserLanguage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "language", table: "user_preferences", type: "character varying(2)", maxLength: 2, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "language", table: "user_preferences");
    }
}
