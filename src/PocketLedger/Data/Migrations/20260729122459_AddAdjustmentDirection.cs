using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdjustmentDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "adjustment_direction",
                table: "transactions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_adjustment_direction",
                table: "transactions",
                sql: "(type = 'Adjustment' AND adjustment_direction IS NOT NULL) OR (type <> 'Adjustment' AND adjustment_direction IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_adjustment_direction",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "adjustment_direction",
                table: "transactions");
        }
    }
}
