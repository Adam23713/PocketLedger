using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PocketLedger.Data;

#nullable disable

namespace PocketLedger.Infrastructure.Data.Migrations;

[DbContext(typeof(PocketLedgerDbContext))]
[Migration("20261019120000_AddBlockedUsers")]
public sealed class AddBlockedUsers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "blocked_users",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                blocked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_blocked_users", x => x.user_id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "blocked_users");
}
