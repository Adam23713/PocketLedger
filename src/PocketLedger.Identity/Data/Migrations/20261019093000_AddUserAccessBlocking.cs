using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PocketLedger.Data;

namespace PocketLedger.Identity.Data.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20261019093000_AddUserAccessBlocking")]
public sealed class AddUserAccessBlocking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "IsAccessBlocked", table: "AspNetUsers", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.CreateTable(name: "DeletedUserTombstones", columns: table => new
        {
            UserId = table.Column<Guid>(type: "uuid", nullable: false),
            DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_DeletedUserTombstones", item => item.UserId));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DeletedUserTombstones");
        migrationBuilder.DropColumn(name: "IsAccessBlocked", table: "AspNetUsers");
    }
}
