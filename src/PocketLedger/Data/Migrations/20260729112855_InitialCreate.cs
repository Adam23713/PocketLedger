using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    initial_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    include_in_main_balance = table.Column<bool>(type: "boolean", nullable: false),
                    include_in_net_worth = table.Column<bool>(type: "boolean", nullable: false),
                    include_in_statistics = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    parent_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.CheckConstraint("ck_categories_not_self_referencing", "parent_category_id IS NULL OR parent_category_id <> id");
                    table.CheckConstraint("ck_categories_subcategory_icon", "parent_category_id IS NULL OR icon IS NULL");
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_category_id",
                        column: x => x.parent_category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.CheckConstraint("ck_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_transactions_different_accounts", "target_account_id IS NULL OR target_account_id <> account_id");
                    table.CheckConstraint("ck_transactions_target_amount_positive", "target_amount IS NULL OR target_amount > 0");
                    table.CheckConstraint("ck_transactions_target_amount_transfer", "target_amount IS NULL OR type = 'Transfer'");
                    table.CheckConstraint("ck_transactions_transfer_target", "(type = 'Transfer' AND target_account_id IS NOT NULL) OR (type <> 'Transfer' AND target_account_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_transactions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_accounts_target_account_id",
                        column: x => x.target_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_display_order",
                table: "accounts",
                column: "display_order");

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_category_id",
                table: "categories",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_type_display_order",
                table: "categories",
                columns: new[] { "type", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_account_id_transaction_date",
                table: "transactions",
                columns: new[] { "account_id", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_category_id_transaction_date",
                table: "transactions",
                columns: new[] { "category_id", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_target_account_id",
                table: "transactions",
                column: "target_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_transaction_date",
                table: "transactions",
                column: "transaction_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "categories");
        }
    }
}
