using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTransactionAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "automation_starts_on",
                table: "recurring_transactions",
                type: "date",
                nullable: false,
                defaultValueSql: "(CURRENT_TIMESTAMP AT TIME ZONE 'Europe/Budapest')::date");

            migrationBuilder.CreateTable(
                name: "recurring_transaction_occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recurring_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_transaction_occurrences", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_occurrences_recurring_transaction_id",
                        column: x => x.recurring_transaction_id,
                        principalTable: "recurring_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_occurrences_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_occurrences_owner_id",
                table: "recurring_transaction_occurrences",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ux_recurring_transaction_occurrences_template_date",
                table: "recurring_transaction_occurrences",
                columns: new[] { "recurring_transaction_id", "occurrence_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_transaction_occurrences");

            migrationBuilder.DropColumn(
                name: "automation_starts_on",
                table: "recurring_transactions");
        }
    }
}
