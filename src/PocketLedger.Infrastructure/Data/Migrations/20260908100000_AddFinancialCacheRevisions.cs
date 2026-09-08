using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Infrastructure.Data.Migrations;

public partial class AddFinancialCacheRevisions : Migration
{
    private static readonly string[] Tables = ["accounts", "categories", "transactions", "debts", "recurring_transactions", "recurring_transaction_occurrences"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE financial_cache_revisions (owner_id uuid PRIMARY KEY, revision uuid NOT NULL);
            CREATE FUNCTION update_financial_cache_revision() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    INSERT INTO financial_cache_revisions (owner_id, revision)
                    SELECT owner_id, gen_random_uuid() FROM (SELECT DISTINCT owner_id FROM old_rows) owners ORDER BY owner_id
                    ON CONFLICT (owner_id) DO UPDATE SET revision = EXCLUDED.revision;
                ELSIF TG_OP = 'UPDATE' THEN
                    INSERT INTO financial_cache_revisions (owner_id, revision)
                    SELECT owner_id, gen_random_uuid() FROM (SELECT owner_id FROM old_rows UNION SELECT owner_id FROM new_rows) owners ORDER BY owner_id
                    ON CONFLICT (owner_id) DO UPDATE SET revision = EXCLUDED.revision;
                ELSE
                    INSERT INTO financial_cache_revisions (owner_id, revision)
                    SELECT owner_id, gen_random_uuid() FROM (SELECT DISTINCT owner_id FROM new_rows) owners ORDER BY owner_id
                    ON CONFLICT (owner_id) DO UPDATE SET revision = EXCLUDED.revision;
                END IF;
                RETURN NULL;
            END;
            $$;
            """);
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"CREATE TRIGGER financial_cache_insert AFTER INSERT ON {table} REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION update_financial_cache_revision();");
            migrationBuilder.Sql($"CREATE TRIGGER financial_cache_update AFTER UPDATE ON {table} REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION update_financial_cache_revision();");
            migrationBuilder.Sql($"CREATE TRIGGER financial_cache_delete AFTER DELETE ON {table} REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION update_financial_cache_revision();");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"DROP TRIGGER financial_cache_insert ON {table}; DROP TRIGGER financial_cache_update ON {table}; DROP TRIGGER financial_cache_delete ON {table};");
        }
        migrationBuilder.Sql("DROP FUNCTION update_financial_cache_revision(); DROP TABLE financial_cache_revisions;");
    }
}
