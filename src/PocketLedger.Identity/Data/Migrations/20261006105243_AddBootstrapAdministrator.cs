using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBootstrapAdministrator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBootstrapAdministrator",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "AspNetUsers"
                SET "IsBootstrapAdministrator" = TRUE
                WHERE "Id" = (
                    SELECT "Id"
                    FROM "AspNetUsers"
                    ORDER BY "CreatedAtUtc", "Id"
                    LIMIT 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_IsBootstrapAdministrator",
                table: "AspNetUsers",
                column: "IsBootstrapAdministrator",
                unique: true,
                filter: "\"IsBootstrapAdministrator\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_IsBootstrapAdministrator",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsBootstrapAdministrator",
                table: "AspNetUsers");
        }
    }
}
