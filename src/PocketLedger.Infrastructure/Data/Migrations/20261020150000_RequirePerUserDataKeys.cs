using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PocketLedger.Data;

#nullable disable

namespace PocketLedger.Infrastructure.Data.Migrations;

// No schema change: user fields switch to a per-user inner layer (PocketLedger.Database.v2), which existing rows do not have.
// Per-user encryption starts from a fresh installation, so a populated database is refused rather than left unreadable.
[DbContext(typeof(PocketLedgerDbContext))]
[Migration("20261020150000_RequirePerUserDataKeys")]
public sealed class RequirePerUserDataKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM "accounts")
                    OR EXISTS (SELECT 1 FROM "categories")
                    OR EXISTS (SELECT 1 FROM "transactions")
                    OR EXISTS (SELECT 1 FROM "recurring_transactions")
                    OR EXISTS (SELECT 1 FROM "debts")
                    OR EXISTS (SELECT 1 FROM "planner_items")
                    OR EXISTS (SELECT 1 FROM "planner_months")
                    OR EXISTS (SELECT 1 FROM "user_preferences") THEN
                    RAISE EXCEPTION 'Per-user encryption requires an empty finance database. Start from a fresh installation and restore an encrypted .plbackup; see docs/database-encryption.md.';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
