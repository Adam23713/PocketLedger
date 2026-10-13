# Deploying PocketLedger to a VPS

This is the complete walkthrough for running PocketLedger in production on your own server with Docker Compose, Cloudflare in front of it, and the full encryption stack enabled. For a one-paragraph version, see the [Quick start](../README.md#quick-start-docker-compose) in the main README.

- [Requirements](#requirements)
- [1. Domains and secrets](#1-domains-and-secrets)
- [2. Encryption keys and internal TLS](#2-encryption-keys-and-internal-tls)
- [3. Build and bootstrap](#3-build-and-bootstrap)
- [4. Start the deployment](#4-start-the-deployment)
- [Internal API TLS](#internal-api-tls)
- [Security systems overview](#security-systems-overview)
- [Moving an existing deployment to the app subdomain](#moving-an-existing-deployment-to-the-app-subdomain)
- [Financial cache (Valkey)](#financial-cache-valkey)
- [Request telemetry (Valkey)](#request-telemetry-valkey)
- [Backups](#backups)
- [Volumes](#volumes)

## Requirements

- Docker Engine and Docker Compose v2
- A domain with DNS records proxied through Cloudflare (PocketLedger's reference edge setup assumes this — see [Edge security](edge-security.md))
- A TOTP authenticator app for the first login

PocketLedger runs as four services — `landing`, `web`, `api`, `identity` — each built from the same `Dockerfile` with a different target, plus `caddy`, `crowdsec`, Valkey and three isolated PostgreSQL databases. See the [architecture overview](../README.md#architecture) for how they relate.

## 1. Domains and secrets

Copy the environment and Caddy templates:

```bash
cp .env.example .env
cp Caddyfile.example Caddyfile
```

In `.env`, set all four domain names and replace every placeholder secret:

- `POCKETLEDGER_WEB_CLIENT_SECRET` — a long random string.
- `POCKETLEDGER_SIGNING_KEY` — at least 32 random bytes, Base64-encoded: `openssl rand -base64 64`.
- `POSTGRES_PASSWORD` — a strong database password.
- `POCKETLEDGER_INITIAL_USERNAME` / `POCKETLEDGER_INITIAL_PASSWORD` — the first account, created at bootstrap.
- `POCKETLEDGER_PUBLIC_REGISTRATION_ENABLED` — `true` to allow new password and configured social-provider registrations; defaults to `false`. Existing linked external accounts can still sign in while registration is disabled.
- `POCKETLEDGER_GOOGLE_CLIENT_ID` / `POCKETLEDGER_GOOGLE_CLIENT_SECRET` — optional Google OAuth web client credentials. Create the client in Google Cloud Console and add `https://<POCKETLEDGER_IDENTITY_DOMAIN>/signin-google` as an authorized redirect URI. Leave both values empty to keep Google login disabled; never commit the production secret.
- `POCKETLEDGER_FACEBOOK_APP_ID` / `POCKETLEDGER_FACEBOOK_APP_SECRET` — optional Meta app credentials for Facebook Login. Add `https://<POCKETLEDGER_IDENTITY_DOMAIN>/signin-facebook` as a valid OAuth redirect URI. Leave both values empty to keep Facebook login disabled; never commit the app secret.
- `POCKETLEDGER_APPLE_CLIENT_ID` / `POCKETLEDGER_APPLE_TEAM_ID` / `POCKETLEDGER_APPLE_KEY_ID` — the Services ID, Apple Developer Team ID and Sign in with Apple private-key ID. Leave any value empty to keep Apple login disabled.
- `POCKETLEDGER_APPLE_PRIVATE_KEY_FILE` — absolute host path to the Apple `.p8` private-key file. Compose mounts this file read-only; the key contents must never be placed in `.env` or committed to source control.
- `CROWDSEC_API_KEY` — `openssl rand -hex 32`. Review the [edge security guide](edge-security.md) for the rest of the Cloudflare and CrowdSec setup, including verification, monitoring, key rotation and rollback.
- `CROWDSEC_IDENTITY_API_KEY` — a second `openssl rand -hex 32` value used only by the Identity Admin dashboard for read-only LAPI decision queries. Never reuse `CROWDSEC_API_KEY`.

For Google login, configure the OAuth consent screen and create a **Web application** OAuth client in Google Cloud Console. Register the exact production callback `https://<identity-domain>/signin-google`; for direct local development also register `http://localhost:5052/signin-google`. Google login appears only when both credential values are non-empty. PocketLedger requests only the `openid` and `email` scopes and accepts a new external account only when Google reports its email as verified.

For Facebook login, create a Meta app with the **Facebook Login** product, configure its basic App ID and App Secret, and add the exact production callback `https://<identity-domain>/signin-facebook` under **Valid OAuth Redirect URIs**. For direct local development, also register `http://localhost:5052/signin-facebook`. Facebook login appears only when both credential values are non-empty. PocketLedger requests only the `email` permission and the `id` and `email` profile fields. A Facebook account that does not return a syntactically valid email is rejected rather than linked or registered; Meta test users and app roles may be required while the app remains in development mode.

For Sign in with Apple, create and configure a **Services ID**, associate it with the primary App ID, and register the exact HTTPS return URL `https://<identity-domain>/signin-apple` in Apple Developer Portal. Apple does not accept plain HTTP or `localhost` as a web return URL; local end-to-end testing therefore needs an HTTPS custom hostname or tunnel whose exact callback is registered with Apple. Create a Sign in with Apple private key, store the downloaded `.p8` file outside the repository with restrictive host permissions, and set only its host path in `POCKETLEDGER_APPLE_PRIVATE_KEY_FILE`. PocketLedger mounts the file read-only and generates the Apple client secret at runtime; it requests only `openid` and `email`, keeps ID-token validation enabled, and uses Apple's stable `sub` claim as the external account key.

Apple may provide the user's name only during the first authorization, and users can choose a private relay address with Hide My Email. PocketLedger does not depend on the name and keeps subsequent access linked by `sub`; registration fails closed unless the validated ID token contains a syntactically valid email with `email_verified=true`. Save no Apple tokens or demo credentials in configuration or logs. To rotate credentials, create the replacement `.p8` key before revoking the old one, update both `POCKETLEDGER_APPLE_KEY_ID` and the mounted file path, recreate the Identity container, verify a full login, and only then revoke the previous key. The generated client secret is short-lived operational material and is regenerated by the provider; rotating the signing key and restarting Identity rotates future generated secrets.

### Registration and demo-account validation

After the provider callbacks and secrets are configured, set `POCKETLEDGER_PUBLIC_REGISTRATION_ENABLED=true` and recreate the Identity container. Validate each route with a separate disposable identity; never record passwords, recovery codes, provider tokens or Apple private-key contents in source control, tickets or logs.

- **Password:** register a unique username and e-mail, complete TOTP setup, sign out, then sign in again with the password.
- **Google:** register with a Google test account, complete TOTP setup, sign out, then confirm the same Google account returns to the existing PocketLedger account.
- **Facebook:** repeat with a Meta test user that supplies an e-mail address; confirm a cancelled consent flow creates no PocketLedger user.
- **Apple:** repeat with an Apple test account, including Hide My Email when available; confirm a later sign-in still resolves by Apple's stable subject when name/e-mail are no longer returned.

For every route, open the application after TOTP enrollment and verify that the account sees only its own empty or deliberately seeded data. Also verify that attempting a different registration route with an e-mail already used by another PocketLedger account is rejected rather than automatically linked. Disable public registration again after validation if the deployment is not intended to accept general sign-ups.

Keep `COMPOSE_PROJECT_NAME` stable for the lifetime of the deployment. Renaming a checkout without an explicit project name can select a different set of Compose-managed database volumes — Compose uses the project name to identify its containers, networks and named volumes, not the directory path.

## 2. Encryption keys and internal TLS

PocketLedger encrypts selected financial and Identity fields before they reach PostgreSQL (see [Security systems overview](#security-systems-overview) below), and every hop to the API is encrypted with a private internal CA. Both need to exist before the first start:

```bash
sudo bash tools/initialize-encryption-keys.sh /opt/pocketledger-security
sudo bash tools/initialize-internal-tls.sh /opt/pocketledger-security
```

Both scripts refuse to overwrite an existing directory. The first creates separate `api`, `web` and `identity` key/certificate directories; the second creates the internal CA and the API's TLS certificate. Set `POCKETLEDGER_SECURITY_DIRECTORY` in `.env` to match the path you used — Compose mounts only each host's own subdirectory, and mount sources must already exist.

For the full design, field-by-field data classification, and key rotation, see the [database encryption guide](database-encryption.md).

## 3. Build and bootstrap

```bash
docker compose build
docker compose up -d identity-database
docker compose run --rm identity bootstrap-identity
```

This builds all four application images and creates the first Identity account from `POCKETLEDGER_INITIAL_USERNAME` / `POCKETLEDGER_INITIAL_PASSWORD`. Public registration remains disabled by default; enable it explicitly with `POCKETLEDGER_PUBLIC_REGISTRATION_ENABLED=true` only after the required provider callbacks, secrets and monitoring are ready.

The bootstrap command permanently marks this first account as the sole administrator. The flag cannot be granted, revoked, or transferred through the UI, API, or account commands, and the database rejects a second administrator. When upgrading an existing deployment, the Identity migration assigns the flag to the oldest user by `CreatedAtUtc`, using the user ID as a deterministic tie-breaker. Existing sessions must sign in again to receive the administrator claim. Review the selected account before exposing future administrator endpoints; changing the designation is intentionally unsupported.

## 4. Start the deployment

```bash
docker compose up -d
```

On first login, configure TOTP and save the generated recovery codes. A finance dataset previously exported as an encrypted `.plbackup` file can be restored from **Import / Export**.

If you plan to use OCI KMS for key custody instead of the local certificate provider, finish and verify this local deployment first, then follow the [Local-to-OCI activation procedure](oci-kms.md#activate-oci-kms-on-an-existing-clean-local-deployment). Don't enable the OCI Compose override during initial database creation or Identity bootstrap — the two migrations are deliberately kept separate.

## Internal API TLS

Kestrel serves the API only on `https://api:5051`; the Web/BFF and Caddy validate the API's certificate against the mounted internal CA and its `api` DNS name. The CA's private key is never mounted into any container. A missing CA, an HTTP API URL, an untrusted certificate, or a certificate whose DNS name isn't `api` fails closed rather than silently falling back to plaintext.

To add internal TLS to a deployment that already has encryption keys configured:

```bash
# Stop the application containers first
sudo bash tools/initialize-internal-tls.sh "$POCKETLEDGER_SECURITY_DIRECTORY"
# Deploy the updated Compose and Caddy configuration together, then recreate api, web, caddy
```

The script refuses to overwrite existing TLS material. Back up `internal-tls/ca.key` — it's required to renew the API certificate. Treat `internal-tls/api.pfx` as a private key and never commit either file.

Before completing a rollout, verify that `docker compose config --quiet` succeeds, all containers stay running, Web can load authenticated financial pages, an encrypted export succeeds, and the public API health endpoint responds through Caddy.

## Security systems overview

PocketLedger layers several independent protections rather than relying on any single one. Each has its own detailed guide; this is the map between them.

**Field-level database encryption.** Transaction notes, account and category names, debts, planner snapshots and Identity secrets (TOTP keys, recovery codes, audit metadata) are encrypted before EF Core writes them to PostgreSQL, using certificate-protected ASP.NET Core Data Protection key rings — one per service. There is no setting to disable this and no plaintext fallback on a decryption error. Amounts, dates, relationships and other low-sensitivity or necessarily-queryable columns are left as typed, indexed columns so filtering, joins and balance calculations keep working. → [Database encryption guide](database-encryption.md)

**Full-disk encryption (not part of this deployment).** Field encryption alone doesn't protect a stolen disk: amounts, dates, relationships and database metadata stay readable in a raw copy. Tools like LUKS can close that gap by encrypting the block device PostgreSQL's data lives on, but this isn't something PocketLedger ships or tests — it's a reasonable addition if you want it, planned and verified independently for your own VPS and storage layout. → [Scope and guarantees](database-encryption.md#scope-and-guarantees)

**Cloud HSM key custody (OCI Vault/KMS, optional).** Instead of a certificate on local disk, each service's Data Protection key ring can be wrapped by its own non-exportable AES-256 key inside an OCI HSM, with a dedicated IAM user and least-privilege policy per service. OCI only ever receives key-ring XML for wrap/unwrap — application data never leaves the VPS. Activation requires a passphrase-protected unlock after every restart (`./tools/unlock-oci-kms.sh`); there's no automatic unlock and no provider fallback to `Local`. → [OCI KMS guide](oci-kms.md)

**Internal TLS.** Covered [above](#internal-api-tls) — every hop between Web, Caddy and the API is encrypted with a private CA, independent of the public-facing TLS terminated by Caddy/Cloudflare.

**Edge protection.** Caddy and CrowdSec sit in front of all four public hosts, behind Cloudflare. Only Cloudflare's published IP ranges are trusted for client-IP headers, a shared rate limiter caps requests per client across all hosts (with a stricter limit on Identity's authentication endpoints), and CrowdSec bans IPs that match flood or abuse patterns using only local detection — no data is shared with CrowdSec's community service. → [Edge security guide](edge-security.md)

**Authentication.** Every account requires TOTP-based two-factor authentication plus recovery codes. Public password and configured social-provider registration is controlled by `POCKETLEDGER_PUBLIC_REGISTRATION_ENABLED` and is disabled by default. Existing external accounts remain linked by provider and stable subject; matching e-mail addresses are never linked automatically.

Together these mean a single leaked backup file, a stolen disk, or a flood of malicious requests each run into a layer built specifically to stop them, rather than one boundary doing all the work.

## Moving an existing deployment to the app subdomain

If you're moving from a single-domain deployment to the recommended `app.` subdomain topology:

1. Set `POCKETLEDGER_LANDING_DOMAIN` to the public domain and `POCKETLEDGER_WEB_DOMAIN` to the app subdomain.
2. Update the deployed Caddyfile from `Caddyfile.example`, add the new DNS record in Cloudflare, and confirm HTTPS works on both hosts (Cloudflare Full (strict) to the origin).
3. Keep both origins on the same site (e.g. `pocketledger.dev` and `app.pocketledger.dev`) so the existing `SameSite=Lax` app cookie still works for the landing page's session check.
4. Roll out Identity, Web, landing and the proxy configuration together.

On Identity startup, the existing Web OIDC client's login/logout redirect URIs are automatically synchronized with `OpenIddict:WebBaseUrl`; its client secret and permissions are preserved. Existing host-only browser cookies do **not** move to the new subdomain, so users may need to sign in again, and bookmarks to app paths on the old domain should be updated.

`Landing:AppBaseUrl` configures the landing page's app links; `Landing:BaseUrl` on Web configures the profile-menu website link and the exact allowed CORS origin for `GET /Session/Status` — an endpoint that returns only an authentication boolean with no-store caching and shares no cookies or tokens with the landing page. Configure Cloudflare to bypass caching for `/Session/*` on the app domain, and don't apply public-page caching rules to authenticated app routes. When the app can't be reached, the landing page keeps a working login link.

## Financial cache (Valkey)

The API uses Valkey to cache current/previous-month transaction queries (filters, pages, daily totals included), statistics for the current month and preceding 11 months, and current account/main balances — bounded by the user's own time zone for month eligibility. Older or unbounded queries always go straight to PostgreSQL.

Production Compose includes a private Valkey service with a 256 MB eviction limit and no persistence. For a locally running API:

```bash
docker compose -f compose.yaml -f compose.development.yaml up -d valkey
# set FinancialCache__ConnectionString=localhost:6379
```

An absent connection string disables caching entirely. `FinancialCache__LifetimeMinutes` defaults to 30 (range 1–1440) and only bounds unused entries — writes invalidate the relevant cache immediately via a per-user revision that database triggers rotate on any account, category, transaction, debt or recurring-data change, including backup restores. A concurrent old read or a Valkey outage can't make stale entries current again, and Valkey failures fall back transparently to PostgreSQL.

Apply API migrations before enabling caching. The PostgreSQL/Valkey integration tests run in CI; to run them locally, set `PL_TEST_POSTGRES` (to a connection string whose role can create databases) and `PL_TEST_VALKEY`, then run:

```bash
dotnet test tests/PocketLedger.Tests --filter FullyQualifiedName~FinancialCacheTests
```

## Request telemetry (Valkey)

Web, API and Identity aggregate relevant dynamic requests into five-minute Valkey hashes for the security dashboard. Each hash stores only counters: total, anonymous/unattributed and authenticated user ID counts. Paths, query strings, headers, cookies, tokens and request or response bodies are never stored. Health endpoints, static assets, OpenAPI/Swagger and OpenID Connect discovery requests are excluded by the shared classifier.

Production Compose configures all three applications with `RequestTelemetry__ConnectionString=valkey:6379`. For applications run directly on the host, set it to `localhost:6379`; leaving it unset disables collection and makes queries return empty results. `RequestTelemetry__RetentionHours` defaults to and cannot exceed 48, while `RequestTelemetry__BucketMinutes` is fixed at 5 so later dashboard consumers can reuse the same bucket contract.

Recording is placed after authentication in every pipeline. Requests enqueue an in-memory observation and never wait for Valkey; a background worker coalesces observations before incrementing bucket counters. Buckets receive an absolute expiry at their start time plus the configured retention, so no bucket remains queryable after 48 hours. A full queue or a Valkey connection/write failure drops telemetry and logs a rate-limited warning without affecting the application request.

Query intervals use an inclusive start and exclusive end. Inputs are expanded to whole five-minute UTC buckets, and the returned `FromUtc`/`ToUtc` expose those effective aligned boundaries. `AverageRequestsPerMinute` uses the selected user's count when a user filter is present, otherwise total volume, divided by the complete aligned interval in minutes. `IsAvailable` distinguishes a successful zero-count query from disabled configuration or a Valkey read failure.

## Backups

Encrypted `.plbackup` files contain the signed-in user's complete finance dataset — accounts, categories, transactions, recurring entries, debts, planner history — but never account passwords, TOTP secrets, recovery codes, authentication audit events, or BFF session tokens. Restoring one is independent of which encryption-key provider (local certificate or OCI KMS) is active; see the [database encryption guide](database-encryption.md#key-rotation-and-recovery) for key-ring backup and recovery, which is a separate concern from finance data backup.

## Volumes

The three named database volumes are `web-postgres-data`, `api-postgres-data`, and `identity-postgres-data`. `docker compose down` preserves them; `docker compose down --volumes` permanently deletes all three databases. Caddy and CrowdSec have their own volumes (`caddy-data`, `caddy-config`, `caddy-logs`, `crowdsec-config`, `crowdsec-data`) — see [Edge security](edge-security.md#versions-and-storage) for what each one holds and how it should be protected.
