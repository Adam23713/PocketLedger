using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDebts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_transfer_target",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recurring_transactions_category",
                table: "recurring_transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "debt_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "debt_operation_type",
                table: "transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "debt_id",
                table: "recurring_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "debt_operation_type",
                table: "recurring_transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transaction_id",
                table: "recurring_transaction_occurrences",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "debts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    counterparty_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debts", x => x.id);
                    table.CheckConstraint("ck_debts_date_range", "due_date IS NULL OR due_date >= start_date");
                    table.CheckConstraint("ck_debts_original_amount_positive", "original_amount > 0");
                    table.CheckConstraint("ck_debts_receivable_type", "direction <> 'Receivable' OR type = 'PrivatePerson'");
                    table.ForeignKey(
                        name: "fk_debts_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_debts_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_debt_id_transaction_date",
                table: "transactions",
                columns: new[] { "debt_id", "transaction_date" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_account",
                table: "transactions",
                sql: "(type = 'DebtEntry' AND account_id IS NULL) OR (type <> 'DebtEntry' AND account_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_debt_operation",
                table: "transactions",
                sql: "(debt_id IS NULL AND debt_operation_type IS NULL) OR (debt_id IS NOT NULL AND debt_operation_type IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_transfer_target",
                table: "transactions",
                sql: "(type = 'Transfer' AND account_id IS NOT NULL AND target_account_id IS NOT NULL) OR (type <> 'Transfer' AND target_account_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_debt_id",
                table: "recurring_transactions",
                column: "debt_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurring_transactions_category",
                table: "recurring_transactions",
                sql: "(debt_id IS NOT NULL AND category_id IS NULL) OR (debt_id IS NULL AND type IN ('Income', 'Expense') AND category_id IS NOT NULL) OR (debt_id IS NULL AND type = 'Adjustment' AND category_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_recurring_transaction_occurrences_transaction_id",
                table: "recurring_transaction_occurrences",
                column: "transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_debts_account_id",
                table: "debts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_debts_owner_id_status",
                table: "debts",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.AddForeignKey(
                name: "fk_recurring_transaction_occurrences_transactions_transaction_id",
                table: "recurring_transaction_occurrences",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_recurring_transactions_debts_debt_id",
                table: "recurring_transactions",
                column: "debt_id",
                principalTable: "debts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_debts_debt_id",
                table: "transactions",
                column: "debt_id",
                principalTable: "debts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_recurring_transaction_occurrences_transactions_transaction_id",
                table: "recurring_transaction_occurrences");

            migrationBuilder.DropForeignKey(
                name: "fk_recurring_transactions_debts_debt_id",
                table: "recurring_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_transactions_debts_debt_id",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "debts");

            migrationBuilder.DropIndex(
                name: "ix_transactions_debt_id_transaction_date",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_account",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_debt_operation",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_transfer_target",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_recurring_transactions_debt_id",
                table: "recurring_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recurring_transactions_category",
                table: "recurring_transactions");

            migrationBuilder.DropIndex(
                name: "ux_recurring_transaction_occurrences_transaction_id",
                table: "recurring_transaction_occurrences");

            migrationBuilder.DropColumn(
                name: "debt_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "debt_operation_type",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "debt_id",
                table: "recurring_transactions");

            migrationBuilder.DropColumn(
                name: "debt_operation_type",
                table: "recurring_transactions");

            migrationBuilder.DropColumn(
                name: "transaction_id",
                table: "recurring_transaction_occurrences");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_transfer_target",
                table: "transactions",
                sql: "(type = 'Transfer' AND target_account_id IS NOT NULL) OR (type <> 'Transfer' AND target_account_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurring_transactions_category",
                table: "recurring_transactions",
                sql: "(type IN ('Income', 'Expense') AND category_id IS NOT NULL) OR (type = 'Adjustment' AND category_id IS NULL)");
        }
    }
}
