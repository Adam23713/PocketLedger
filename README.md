# PocketLedger

PocketLedger is a self-hosted personal finance manager built with ASP.NET Core and PostgreSQL. It provides accounts, categorized transactions, recurring entries, loans and debts, calendar views, statistics, CSV import/export, and JSON backup/restore.

## Architecture

PocketLedger runs as three independently built and deployed server processes:

- **PocketLedger.Web** is the server-side Razor MVC application and browser-facing BFF. The browser receives only an encrypted session cookie. OIDC access and refresh tokens are protected and stored in the Web database.
- **PocketLedger.Api** owns financial data and exposes the first-party client API below `/api/v1`. It accepts bearer access tokens issued by PocketLedger.Identity. The recurring transaction worker runs here; only one API instance may run at a time.
- **PocketLedger.Identity** owns users, credentials, TOTP configuration, recovery codes, security audit events, and the OpenIddict authorization server. Public self-registration is intentionally disabled; accounts are bootstrapped or managed administratively.

Each server owns a separate PostgreSQL database. The Web process never connects to the API or Identity database, and the API process never connects to the Web or Identity database.

Supporting projects:

```text
src/PocketLedger.Domain/          Finance entities and value definitions
src/PocketLedger.Application/     Application interfaces and shared business rules
src/PocketLedger.Contracts/       API request and response contracts
src/PocketLedger.Infrastructure/  EF Core finance persistence and service implementations
src/PocketLedger.Web/             Razor MVC / BFF host
src/PocketLedger.Api/             Versioned finance API host
src/PocketLedger.Identity/        Identity and OpenIddict host
```

## URL topology

The recommended public topology is three subdomains behind Cloudflare and Caddy:

```text
https://ledger.example.com           Web/BFF
https://api.ledger.example.com       API
https://identity.ledger.example.com  Identity/OIDC
```

The browser uses the Web/BFF for finance operations. The public API hostname remains available for future first-party clients. Caddy is the only published entry point in the supplied Compose topology; the application containers trust forwarded headers because they are reachable only on the private Compose network.

## Docker Compose deployment

Requirements: Docker Engine, Docker Compose v2, DNS records proxied by Cloudflare, and an authenticator app supporting TOTP.

1. Copy and edit the environment and Caddy templates:

   ```bash
   cp .env.example .env
   cp Caddyfile.example Caddyfile
   ```

2. Set all three domain names and replace every secret. Generate the shared signing key from at least 32 random bytes, Base64 encoded. For example:

   ```bash
   openssl rand -base64 64
   ```

3. Build the images and initialize the Identity database and first user:

   ```bash
   docker compose build
   docker compose up -d identity-database
   docker compose run --rm identity bootstrap-identity
   ```

4. Start the complete deployment:

   ```bash
   docker compose up -d
   ```

On first login, configure TOTP and save the generated recovery codes. Finance data previously exported as JSON can then be restored from **Import / Export**.

The three named database volumes are `web-postgres-data`, `api-postgres-data`, and `identity-postgres-data`. `docker compose down` preserves them; `docker compose down --volumes` permanently removes all three databases.

## Local development

The default settings expect three local PostgreSQL databases:

```text
pocketledger_web
pocketledger_api
pocketledger_identity
```

Restore tools, apply each initial migration, and build:

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/PocketLedger.Infrastructure --startup-project src/PocketLedger.Api --context PocketLedgerDbContext
dotnet tool run dotnet-ef database update --project src/PocketLedger.Identity --startup-project src/PocketLedger.Identity --context IdentityDbContext
dotnet tool run dotnet-ef database update --project src/PocketLedger.Web --startup-project src/PocketLedger.Web --context WebDbContext
dotnet build PocketLedger.slnx
```

Bootstrap the first identity:

```bash
export POCKETLEDGER_INITIAL_USERNAME='demo-admin'
export POCKETLEDGER_INITIAL_PASSWORD='replace-with-a-strong-password'
dotnet run --project src/PocketLedger.Identity -- bootstrap-identity
```

Run the three hosts in separate terminals:

```bash
dotnet run --project src/PocketLedger.Identity
dotnet run --project src/PocketLedger.Api
dotnet run --project src/PocketLedger.Web
```

Development uses one checked-in signing key for interoperability. It is not a production secret. Production must override `OpenIddict__SigningKey` and `Authentication__SigningKey` with the same private Base64 value.

## API and backups

All finance endpoints start with `/api/v1`. OpenAPI metadata is served by the API host at `/openapi/v1.json`. The current API is designed for PocketLedger-owned clients; compatibility is versioned at the URL boundary, while generated clients are intentionally deferred.

JSON backups contain the complete signed-in user's finance dataset but no passwords, TOTP secrets, recovery codes, authentication audit events, or BFF tokens. A fictional importable dataset is available at [`examples/pocketledger-demo.json`](examples/pocketledger-demo.json).

## Validation

Build all projects and run the existing test suite with:

```bash
dotnet build PocketLedger.slnx
dotnet test PocketLedger.slnx
```

## License

PocketLedger is free and open-source software licensed under the [GNU Affero General Public License v3.0 only](LICENSE) (`AGPL-3.0-only`).
