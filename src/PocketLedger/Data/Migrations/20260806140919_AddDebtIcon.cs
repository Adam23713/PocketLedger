using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "icon",
                table: "debts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "other-expense-1");

            migrationBuilder.Sql("UPDATE debts SET icon = 'other-income-1' WHERE direction = 'Receivable'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "icon",
                table: "debts");
        }
    }
}
