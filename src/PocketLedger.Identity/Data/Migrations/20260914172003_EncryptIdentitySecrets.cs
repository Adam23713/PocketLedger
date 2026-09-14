using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketLedger.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class EncryptIdentitySecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AspNetUsers")
                        OR EXISTS (SELECT 1 FROM "AspNetUserTokens")
                        OR EXISTS (SELECT 1 FROM "authentication_audit_events") THEN
                        RAISE EXCEPTION 'Encryption requires an empty database. Restore the finance JSON into a fresh installation; see docs/database-encryption.md.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "UserAgent",
                table: "authentication_audit_events",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RemoteIpAddress",
                table: "authentication_audit_events",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Metadata",
                table: "authentication_audit_events",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ForwardedClientIpAddress",
                table: "authentication_audit_events",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AspNetUsers")
                        OR EXISTS (SELECT 1 FROM "AspNetUserTokens")
                        OR EXISTS (SELECT 1 FROM "authentication_audit_events") THEN
                        RAISE EXCEPTION 'Encryption requires an empty database. Restore the finance JSON into a fresh installation; see docs/database-encryption.md.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "UserAgent",
                table: "authentication_audit_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RemoteIpAddress",
                table: "authentication_audit_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Metadata",
                table: "authentication_audit_events",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ForwardedClientIpAddress",
                table: "authentication_audit_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
