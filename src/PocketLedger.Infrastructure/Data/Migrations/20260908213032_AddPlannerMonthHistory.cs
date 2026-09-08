using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannerMonthHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "copy_day",
                table: "planner_items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "planner_months",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false),
                    opening_balances_json = table.Column<string>(type: "text", nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planner_months", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_planner_months_owner_id_month",
                table: "planner_months",
                columns: new[] { "owner_id", "month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planner_months");

            migrationBuilder.DropColumn(
                name: "copy_day",
                table: "planner_items");
        }
    }
}
