using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannerItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planner_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_date = table.Column<DateOnly>(type: "date", nullable: true),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    account_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planner_items", x => x.id);
                    table.CheckConstraint("ck_planner_items_amounts", "amount > 0 AND account_amount > 0 AND (target_amount IS NULL OR target_amount > 0)");
                    table.CheckConstraint("ck_planner_items_transfer", "(type = 'Transfer' AND target_account_id IS NOT NULL AND target_account_id <> account_id AND target_amount IS NOT NULL AND planned_date IS NOT NULL AND category_id IS NULL) OR (type <> 'Transfer' AND target_account_id IS NULL AND target_amount IS NULL AND category_id IS NOT NULL)");
                    table.CheckConstraint("ck_planner_items_type", "type IN ('Income', 'Expense', 'Transfer')");
                    table.ForeignKey(
                        name: "fk_planner_items_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_planner_items_accounts_target_account_id",
                        column: x => x.target_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_planner_items_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_planner_items_account_id",
                table: "planner_items",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_items_category_id",
                table: "planner_items",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_items_owner_id_month",
                table: "planner_items",
                columns: new[] { "owner_id", "month" });

            migrationBuilder.CreateIndex(
                name: "ix_planner_items_target_account_id",
                table: "planner_items",
                column: "target_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planner_items");
        }
    }
}
