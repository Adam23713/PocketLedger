using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDataKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing users have no data key and could never unlock their data. Per-user encryption starts from a fresh installation.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AspNetUsers") THEN
                        RAISE EXCEPTION 'Per-user data keys require an empty Identity database. Start from a fresh installation; see docs/database-encryption.md.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateTable(
                name: "UserDataKeys",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    PasswordWrap = table.Column<string>(type: "text", nullable: false),
                    RecoveryWrap = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDataKeys", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserDataKeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDataKeys");
        }
    }
}
