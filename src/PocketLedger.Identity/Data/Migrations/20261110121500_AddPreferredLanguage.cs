using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PocketLedger.Data;

namespace PocketLedger.Identity.Data.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20261110121500_AddPreferredLanguage")]
public sealed class AddPreferredLanguage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "PreferredLanguage", table: "AspNetUsers", type: "character varying(2)", maxLength: 2, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PreferredLanguage", table: "AspNetUsers");
    }
}
