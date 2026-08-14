using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiCurrencySettingsAndUserTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "transactions",
                type: "numeric(19,8)",
                precision: 19,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "occurred_at_utc",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "source_currency",
                table: "transactions",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "HUF");

            migrationBuilder.AddColumn<string>(
                name: "target_currency",
                table: "transactions",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AvatarId",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "DefaultCurrency",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                defaultValue: "HUF");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                defaultValue: "Europe/Budapest");

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
                        name: "FK_user_currency_formats_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                UPDATE transactions t SET source_currency = a.currency FROM accounts a WHERE a.id = t.account_id;
                UPDATE transactions t SET source_currency = d.currency FROM debts d WHERE t.account_id IS NULL AND d.id = t.debt_id;
                UPDATE transactions t SET target_currency = a.currency
                FROM accounts a WHERE t.type = 'Transfer' AND a.id = t.target_account_id;
                UPDATE transactions SET exchange_rate = CASE WHEN target_amount IS NULL OR amount = 0 THEN 1 ELSE target_amount / amount END,
                    target_amount = COALESCE(target_amount, amount)
                WHERE type = 'Transfer';
                UPDATE transactions SET occurred_at_utc = (transaction_date + transaction_time) AT TIME ZONE 'Europe/Budapest';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_exchange_rate",
                table: "transactions",
                sql: "(type = 'Transfer' AND exchange_rate > 0 AND target_amount > 0 AND target_currency IS NOT NULL) OR (type <> 'Transfer' AND exchange_rate IS NULL AND target_amount IS NULL AND target_currency IS NULL)");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_currency_formats");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_exchange_rate",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "occurred_at_utc",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "source_currency",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "target_currency",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "AvatarId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DefaultCurrency",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "AspNetUsers");
        }
    }
}
