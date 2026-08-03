using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Data.Migrations;

[DbContext(typeof(PocketLedgerDbContext))]
[Migration("20260731110000_RequireOwnershipStageC")]
public partial class RequireOwnershipStageC : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM accounts WHERE owner_id IS NULL) OR EXISTS (SELECT 1 FROM categories WHERE owner_id IS NULL) OR EXISTS (SELECT 1 FROM transactions WHERE owner_id IS NULL) OR EXISTS (SELECT 1 FROM recurring_transactions WHERE owner_id IS NULL) THEN RAISE EXCEPTION 'Ownership backfill must be completed before Stage C'; END IF; END $$;");
        AlterOwner(migrationBuilder, "accounts", false);
        AlterOwner(migrationBuilder, "categories", false);
        AlterOwner(migrationBuilder, "transactions", false);
        AlterOwner(migrationBuilder, "recurring_transactions", false);
        AddOwnerForeignKey(migrationBuilder, "accounts", "fk_accounts_users_owner_id");
        AddOwnerForeignKey(migrationBuilder, "categories", "fk_categories_users_owner_id");
        AddOwnerForeignKey(migrationBuilder, "transactions", "fk_transactions_users_owner_id");
        AddOwnerForeignKey(migrationBuilder, "recurring_transactions", "fk_recurring_transactions_users_owner_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("fk_accounts_users_owner_id", "accounts");
        migrationBuilder.DropForeignKey("fk_categories_users_owner_id", "categories");
        migrationBuilder.DropForeignKey("fk_transactions_users_owner_id", "transactions");
        migrationBuilder.DropForeignKey("fk_recurring_transactions_users_owner_id", "recurring_transactions");
        AlterOwner(migrationBuilder, "accounts", true);
        AlterOwner(migrationBuilder, "categories", true);
        AlterOwner(migrationBuilder, "transactions", true);
        AlterOwner(migrationBuilder, "recurring_transactions", true);
    }

    private static void AlterOwner(MigrationBuilder migrationBuilder, string table, bool nullable) => migrationBuilder.AlterColumn<Guid>(name: "owner_id", table: table, type: "uuid", nullable: nullable, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: !nullable);
    private static void AddOwnerForeignKey(MigrationBuilder migrationBuilder, string table, string name) => migrationBuilder.AddForeignKey(name: name, table: table, column: "owner_id", principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
}
