using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBudapestRecurringDateDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "automation_starts_on",
                table: "recurring_transactions",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldDefaultValueSql: "(CURRENT_TIMESTAMP AT TIME ZONE 'Europe/Budapest')::date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "automation_starts_on",
                table: "recurring_transactions",
                type: "date",
                nullable: false,
                defaultValueSql: "(CURRENT_TIMESTAMP AT TIME ZONE 'Europe/Budapest')::date",
                oldClrType: typeof(DateOnly),
                oldType: "date");
        }
    }
}
