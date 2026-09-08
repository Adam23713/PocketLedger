# PocketLedger

PocketLedger is a self-hosted personal finance manager built with ASP.NET Core and PostgreSQL. It provides accounts, categorized transactions, recurring entries, loans and debts, calendar views, statistics, CSV import/export, and JSON backup/restore.

## Architecture

PocketLedger runs as four independently deployed components:

- **PocketLedger.Landing** serves the public English landing page with Razor Pages and static assets. It has no database or authentication of its own.
- **PocketLedger.Web** is the server-side Razor MVC application and browser-facing BFF. The browser receives only an encrypted session cookie. OIDC access and refresh tokens are protected and stored in the Web database.
- **PocketLedger.Api** owns financial data and exposes the first-party client API below `/api/v1`. It accepts bearer access tokens issued by PocketLedger.Identity. The recurring transaction worker runs here; only one API instance may run at a time.
- **PocketLedger.Identity** owns users, credentials, TOTP configuration, recovery codes, security audit events, and the OpenIddict authorization server. Public self-registration is intentionally disabled; accounts are bootstrapped or managed administratively.

Web, API and Identity each own a separate PostgreSQL database. The Web process never connects to the API or Identity database, and the API process never connects to the Web or Identity database.

Supporting projects:

```text
src/PocketLedger.Domain/          Finance entities and value definitions
src/PocketLedger.Application/     Application interfaces and shared business rules
src/PocketLedger.Contracts/       API request and response contracts
src/PocketLedger.Infrastructure/  EF Core finance persistence and service implementations
src/PocketLedger.Landing/         Public landing page host
src/PocketLedger.Web/             Razor MVC / BFF host
src/PocketLedger.Api/             Versioned finance API host
src/PocketLedger.Identity/        Identity and OpenIddict host
```

## URL topology

The recommended public topology is a public domain and three subdomains behind Cloudflare and Caddy:

```text
https://pocketledger.dev           Public landing page
https://app.pocketledger.dev       Web/BFF
https://api.pocketledger.dev       API
https://identity.pocketledger.dev  Identity/OIDC
```

The browser uses the Web/BFF for finance operations. The public API hostname remains available for future first-party clients. Caddy is the only published entry point in the supplied Compose topology; the application containers trust forwarded headers because they are reachable only on the private Compose network.

## Docker Compose deployment

Requirements: Docker Engine, Docker Compose v2, DNS records proxied by Cloudflare, and an authenticator app supporting TOTP.

1. Copy and edit the environment and Caddy templates:

   ```bash
   cp .env.example .env
   cp Caddyfile.example Caddyfile
   ```

2. Set all four domain names and replace every secret. Generate the shared signing key from at least 32 random bytes, Base64 encoded. For example:

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

## Moving an existing deployment to the app subdomain

Set `POCKETLEDGER_LANDING_DOMAIN` to the public domain and `POCKETLEDGER_WEB_DOMAIN` to the app subdomain. Update the deployed Caddyfile from the example, add the app DNS record in Cloudflare, and ensure HTTPS works for both hosts (Cloudflare Full (strict) to the origin). Keep the two origins on the same site, such as `pocketledger.dev` and `app.pocketledger.dev`, so the existing SameSite=Lax app cookie works for the landing session check.

On Identity startup, the existing Web OIDC client's login and logout redirect URIs are synchronized with `OpenIddict:WebBaseUrl`; the client secret and permissions are preserved. Roll out the Identity, Web, landing and proxy configuration together. Existing host-only browser cookies do not move to the app subdomain, so users may need to sign in again. Existing bookmarks to application paths on the old domain should be updated to the app subdomain.

`Landing:AppBaseUrl` configures the landing's app links. `Landing:BaseUrl` on Web configures the profile-menu website link and the exact allowed CORS origin for `GET /Session/Status`. This endpoint returns only an authentication boolean with no-store caching; app cookies and tokens are not shared with the landing. Login and logout continue to use the existing OIDC flow and POST antiforgery protection. Configure Cloudflare to bypass caching for `/Session/*` on the app domain; do not apply public-page caching rules to authenticated app routes. When the app cannot be reached, the landing retains a working login link. Contact functionality is deliberately outside PL-81's scope.

## Local development

Run the landing locally with `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PocketLedger.Landing --urls http://localhost:5053`. Its Development configuration links to the Web app on `http://localhost:5050`. The landing itself needs no database.

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

## Financial cache

The API uses Valkey for on-demand caching of current and previous month transaction queries (including filters, pages and daily totals), statistics for the current month and preceding 11 months, and current account/main balances. Month eligibility uses the user's time zone. Older or unbounded transaction queries continue to use PostgreSQL.

Production Compose includes a private Valkey service with a 256 MB eviction limit and no persistence. For a locally running API, start Valkey with `docker compose -f compose.yaml -f compose.development.yaml up -d valkey` and set `FinancialCache__ConnectionString=localhost:6379`. An absent connection string disables caching. `FinancialCache__LifetimeMinutes` defaults to 30 (allowed range: 1–1440); expiration bounds unused entries, while writes invalidate immediately.

Apply API migrations before enabling caching. Database triggers atomically rotate a per-user revision whenever accounts, categories, transactions, debts or recurring data change, including backup restores and background processing. Cache reads check this small indexed revision row instead of repeating the financial queries. Keys include the revision and query dimensions; a concurrent old fill or a Valkey outage cannot make old entries current again. Invalidation deliberately covers all financial cache entries for the affected user. Valkey failures fall back to PostgreSQL.

The PostgreSQL/Valkey integration tests run in CI. To run them locally, set `PL_TEST_POSTGRES` to a test PostgreSQL connection string whose role can create databases, and `PL_TEST_VALKEY` to a test Valkey endpoint, then run `dotnet test tests/PocketLedger.Tests --filter FullyQualifiedName~FinancialCacheTests`. The fixture creates and drops its own uniquely named database. Without both variables these integration tests are reported as skipped.
