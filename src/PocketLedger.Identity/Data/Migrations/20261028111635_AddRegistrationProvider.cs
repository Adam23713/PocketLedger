using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegistrationProvider",
                table: "AspNetUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Existing accounts: the bootstrap administrator, then the first external login, otherwise a local password registration.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" AS u SET "RegistrationProvider" = CASE
                    WHEN u."IsBootstrapAdministrator" THEN 'Bootstrap'
                    WHEN EXISTS (SELECT 1 FROM "AspNetUserLogins" l WHERE l."UserId" = u."Id")
                        THEN (SELECT l."LoginProvider" FROM "AspNetUserLogins" l WHERE l."UserId" = u."Id" ORDER BY l."LoginProvider" LIMIT 1)
                    WHEN u."PasswordHash" IS NOT NULL THEN 'Password'
                    ELSE NULL END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegistrationProvider",
                table: "AspNetUsers");
        }
    }
}
