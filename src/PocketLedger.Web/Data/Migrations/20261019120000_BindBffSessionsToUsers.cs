using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Web.Data.Migrations;

[DbContext(typeof(WebDbContext))]
[Migration("20261019120000_BindBffSessionsToUsers")]
public sealed class BindBffSessionsToUsers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing sessions cannot be attributed to a user, so they could not be revoked; sign everyone out once.
        migrationBuilder.Sql("DELETE FROM bff_sessions;");

        migrationBuilder.AddColumn<Guid>(name: "user_id", table: "bff_sessions", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "revoked_at_utc", table: "bff_sessions", type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_bff_sessions_user_id", table: "bff_sessions", column: "user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_bff_sessions_user_id", table: "bff_sessions");
        migrationBuilder.DropColumn(name: "revoked_at_utc", table: "bff_sessions");
        migrationBuilder.DropColumn(name: "user_id", table: "bff_sessions");
    }
}
