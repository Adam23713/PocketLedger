using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    adjustment_direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    first_occurrence = table.Column<DateOnly>(type: "date", nullable: false),
                    last_occurrence = table.Column<DateOnly>(type: "date", nullable: true),
                    frequency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_transactions", x => x.id);
                    table.CheckConstraint("ck_recurring_transactions_adjustment_direction", "(type = 'Adjustment' AND adjustment_direction IS NOT NULL) OR (type <> 'Adjustment' AND adjustment_direction IS NULL)");
                    table.CheckConstraint("ck_recurring_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_recurring_transactions_category", "(type IN ('Income', 'Expense') AND category_id IS NOT NULL) OR (type = 'Adjustment' AND category_id IS NULL)");
                    table.CheckConstraint("ck_recurring_transactions_date_range", "last_occurrence IS NULL OR last_occurrence >= first_occurrence");
                    table.ForeignKey(
                        name: "fk_recurring_transactions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transactions_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_account_id",
                table: "recurring_transactions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_category_id",
                table: "recurring_transactions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_enabled_first_occurrence",
                table: "recurring_transactions",
                columns: new[] { "enabled", "first_occurrence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_transactions");
        }
    }
}
