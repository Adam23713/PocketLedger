using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    initial_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
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
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "user_preferences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    avatar_id = table.Column<int>(type: "integer", nullable: false),
                    default_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_preferences", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "debts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                });

            migrationBuilder.CreateTable(
                name: "user_currency_formats",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    decimal_places = table.Column<int>(type: "integer", nullable: false),
                    decimal_separator = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    thousands_separator = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    currency_display = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    currency_position = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    use_space = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_currency_formats", x => new { x.user_id, x.currency_code });
                    table.CheckConstraint("ck_user_currency_formats_decimal_places", "decimal_places BETWEEN 0 AND 4");
                    table.CheckConstraint("ck_user_currency_formats_separators", "decimal_separator <> thousands_separator");
                    table.ForeignKey(
                        name: "FK_user_currency_formats_user_preferences_user_id",
                        column: x => x.user_id,
                        principalTable: "user_preferences",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recurring_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    adjustment_direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    first_occurrence = table.Column<DateOnly>(type: "date", nullable: false),
                    last_occurrence = table.Column<DateOnly>(type: "date", nullable: true),
                    automation_starts_on = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "(CURRENT_TIMESTAMP AT TIME ZONE 'Europe/Budapest')::date"),
                    frequency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    debt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debt_operation_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_transactions", x => x.id);
                    table.CheckConstraint("ck_recurring_transactions_adjustment_direction", "(type = 'Adjustment' AND adjustment_direction IS NOT NULL) OR (type <> 'Adjustment' AND adjustment_direction IS NULL)");
                    table.CheckConstraint("ck_recurring_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_recurring_transactions_category", "(debt_id IS NOT NULL AND category_id IS NULL) OR (debt_id IS NULL AND type IN ('Income', 'Expense') AND category_id IS NOT NULL) OR (debt_id IS NULL AND type = 'Adjustment' AND category_id IS NULL)");
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
                    table.ForeignKey(
                        name: "fk_recurring_transactions_debts_debt_id",
                        column: x => x.debt_id,
                        principalTable: "debts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: true),
                    source_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    target_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    adjustment_direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    transaction_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    debt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debt_operation_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.CheckConstraint("ck_transactions_account", "(type = 'DebtEntry' AND account_id IS NULL) OR (type <> 'DebtEntry' AND account_id IS NOT NULL)");
                    table.CheckConstraint("ck_transactions_adjustment_direction", "(type = 'Adjustment' AND adjustment_direction IS NOT NULL) OR (type <> 'Adjustment' AND adjustment_direction IS NULL)");
                    table.CheckConstraint("ck_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_transactions_debt_operation", "(debt_id IS NULL AND debt_operation_type IS NULL) OR (debt_id IS NOT NULL AND debt_operation_type IS NOT NULL)");
                    table.CheckConstraint("ck_transactions_different_accounts", "target_account_id IS NULL OR target_account_id <> account_id");
                    table.CheckConstraint("ck_transactions_exchange_rate", "(type = 'Transfer' AND exchange_rate > 0 AND target_amount > 0 AND target_currency IS NOT NULL) OR (type <> 'Transfer' AND exchange_rate IS NULL AND target_amount IS NULL AND target_currency IS NULL)");
                    table.CheckConstraint("ck_transactions_target_amount_positive", "target_amount IS NULL OR target_amount > 0");
                    table.CheckConstraint("ck_transactions_target_amount_transfer", "target_amount IS NULL OR type = 'Transfer'");
                    table.CheckConstraint("ck_transactions_transfer_target", "(type = 'Transfer' AND account_id IS NOT NULL AND target_account_id IS NOT NULL) OR (type <> 'Transfer' AND target_account_id IS NULL)");
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
                    table.ForeignKey(
                        name: "fk_transactions_debts_debt_id",
                        column: x => x.debt_id,
                        principalTable: "debts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recurring_transaction_occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recurring_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true)
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
                        name: "fk_recurring_transaction_occurrences_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_owner_id_display_order",
                table: "accounts",
                columns: new[] { "owner_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_owner_id_type_display_order",
                table: "categories",
                columns: new[] { "owner_id", "type", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_category_id",
                table: "categories",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_type_display_order",
                table: "categories",
                columns: new[] { "type", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_debts_account_id",
                table: "debts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_debts_owner_id_status",
                table: "debts",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_occurrences_owner_id",
                table: "recurring_transaction_occurrences",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ux_recurring_transaction_occurrences_template_date",
                table: "recurring_transaction_occurrences",
                columns: new[] { "recurring_transaction_id", "occurrence_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recurring_transaction_occurrences_transaction_id",
                table: "recurring_transaction_occurrences",
                column: "transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_account_id",
                table: "recurring_transactions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_category_id",
                table: "recurring_transactions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_debt_id",
                table: "recurring_transactions",
                column: "debt_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_enabled_first_occurrence",
                table: "recurring_transactions",
                columns: new[] { "enabled", "first_occurrence" });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_owner_id_enabled_first_occurrence",
                table: "recurring_transactions",
                columns: new[] { "owner_id", "enabled", "first_occurrence" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_account_id_transaction_date",
                table: "transactions",
                columns: new[] { "account_id", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_category_id_transaction_date",
                table: "transactions",
                columns: new[] { "category_id", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_debt_id_transaction_date",
                table: "transactions",
                columns: new[] { "debt_id", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_owner_id_transaction_date",
                table: "transactions",
                columns: new[] { "owner_id", "transaction_date" });

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
                name: "recurring_transaction_occurrences");

            migrationBuilder.DropTable(
                name: "user_currency_formats");

            migrationBuilder.DropTable(
                name: "recurring_transactions");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "user_preferences");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "debts");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
