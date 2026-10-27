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
- [Blocking and deleting users](#blocking-and-deleting-users)
- [Financial cache (Valkey)](#financial-cache-valkey)
- [Request telemetry (Valkey)](#request-telemetry-valkey)
- [Backups](#backups)
- [Volumes](#volumes)

## Requirements

- Docker Engine and Docker Compose v2
- A domain with DNS records proxied through Cloudflare (PocketLedger's reference edge setup assumes this — see [Edge security](edge-security.md))
- A TOTP authenticator app for the first login

PocketLedger runs as five services — `landing`, `docs`, `web`, `api`, `identity` — each built from the same `Dockerfile` with a different target, plus `caddy`, `crowdsec`, two Valkey instances (`valkey` for the financial cache, `valkey-security` for request telemetry and suspicious request detection) and three isolated PostgreSQL databases. See the [architecture overview](../README.md#architecture) for how they relate.

## 1. Domains and secrets

Copy the environment and Caddy templates:

```bash
cp .env.example .env
cp Caddyfile.example Caddyfile
```

In `.env`, set all five domain names and replace every placeholder secret. The reference documentation hostname is `docs.pocketledger.dev`:

- `POCKETLEDGER_WEB_CLIENT_SECRET` — a long random string.
- `POCKETLEDGER_ADMIN_OPERATIONS_KEY` — a separate long random string used only for authenticated Identity-to-API and Identity-to-Web admin requests (user blocking, session termination and user deletion).
- `POCKETLEDGER_SIGNING_KEY` — at least 32 random bytes, Base64-encoded: `openssl rand -base64 64`.
- `POSTGRES_PASSWORD` — a strong database password.
- `POCKETLEDGER_INITIAL_USERNAME` / `POCKETLEDGER_INITIAL_PASSWORD` — the first account, created at bootstrap.
- `POCKETLEDGER_GOOGLE_CLIENT_ID` / `POCKETLEDGER_GOOGLE_CLIENT_SECRET` — optional Google OAuth web-client credentials. Leave both values empty to keep Google login disabled; never commit the production secret.
- `POCKETLEDGER_FACEBOOK_APP_ID` / `POCKETLEDGER_FACEBOOK_APP_SECRET` — optional Meta app credentials for Facebook Login. Leave both values empty to keep Facebook login disabled; never commit the app secret.
- `CROWDSEC_API_KEY` — `openssl rand -hex 32`. Review the [edge security guide](edge-security.md) for the rest of the Cloudflare and CrowdSec setup, including verification, monitoring, key rotation and rollback.
- `CROWDSEC_IDENTITY_API_KEY` — a second `openssl rand -hex 32` value used only by the Identity Admin dashboard for read-only LAPI decision queries. Never reuse `CROWDSEC_API_KEY`.

For Google login, create a **Web application** OAuth client in Google Auth Platform and register the exact callbacks `https://<identity-domain>/signin-google` for production and `http://localhost:5052/signin-google` for direct local development. PocketLedger requests only the `openid` and `email` scopes, accepts a new external account only when Google reports its e-mail as verified, and links returning users by Google's stable subject rather than by e-mail address. Configure both credential values to enable the provider; a missing value keeps it hidden from the login and registration pages.

For Facebook login, create a Meta app with the **Facebook Login** use case and register the exact callbacks `https://<identity-domain>/signin-facebook` for production and `https://localhost:5052/signin-facebook` for direct local development. PocketLedger requests only the `email` permission and the `id` and `email` fields, rejects responses without a syntactically valid primary e-mail address, and links returning users by Meta's stable user ID. Configure both credential values to enable the provider; a missing value keeps it hidden from the login and registration pages. Use the HTTPS launch profile for local testing because Meta enforces HTTPS redirect URIs. While the Meta app is unpublished, only app roles and test users can sign in.

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

This builds all five application images and creates the first Identity account from `POCKETLEDGER_INITIAL_USERNAME` / `POCKETLEDGER_INITIAL_PASSWORD`. Public self-registration is disabled by default. The administrator opens or closes it and sets the maximum number of accounts on the Identity admin dashboard (`/Admin`), which also shows the registered and free account counts. `POCKETLEDGER_PUBLIC_REGISTRATION_ENABLED` and `POCKETLEDGER_PUBLIC_REGISTRATION_MAXIMUM_USERS` only provide the initial defaults: once the settings are saved on the dashboard, they are stored in the Identity database and take precedence over the environment.

The bootstrap command permanently marks this first account as the sole administrator. The flag cannot be granted, revoked, or transferred through the UI, API, or account commands, and the database rejects a second administrator. When upgrading an existing deployment, the Identity migration assigns the flag to the oldest user by `CreatedAtUtc`, using the user ID as a deterministic tie-breaker. Existing sessions must sign in again to receive the administrator claim. Review the selected account before exposing future administrator endpoints; changing the designation is intentionally unsupported.

## 4. Start the deployment

```bash
docker compose up -d
```

`bootstrap-identity` prints the administrator's data recovery key once: store it offline, separately from the password. Without the password or this key, nobody can recover that account's financial data. On first login, configure TOTP and save the generated recovery codes. A finance dataset previously exported as an encrypted `.plbackup` file can be restored from **Import / Export**.

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

**Field-level database encryption.** Transaction notes, account and category names, debts, planner snapshots and Identity secrets (TOTP keys, recovery codes, audit metadata) are encrypted before EF Core writes them to PostgreSQL, using certificate-protected ASP.NET Core Data Protection key rings — one per service. User data is additionally encrypted with a per-user key that only the user's password or recovery key opens, so the operator cannot read it passively either. There is no setting to disable this and no plaintext fallback on a decryption error. Amounts, dates, relationships and other low-sensitivity or necessarily-queryable columns are left as typed, indexed columns so filtering, joins and balance calculations keep working. → [Database encryption guide](database-encryption.md)

**Full-disk encryption (not part of this deployment).** Field encryption alone doesn't protect a stolen disk: amounts, dates, relationships and database metadata stay readable in a raw copy. Tools like LUKS can close that gap by encrypting the block device PostgreSQL's data lives on, but this isn't something PocketLedger ships or tests — it's a reasonable addition if you want it, planned and verified independently for your own VPS and storage layout. → [Scope and guarantees](database-encryption.md#scope-and-guarantees)

**Cloud HSM key custody (OCI Vault/KMS, optional).** Instead of a certificate on local disk, each service's Data Protection key ring can be wrapped by its own non-exportable AES-256 key inside an OCI HSM, with a dedicated IAM user and least-privilege policy per service. OCI only ever receives key-ring XML for wrap/unwrap — application data never leaves the VPS. Activation requires a passphrase-protected unlock after every restart (`./tools/unlock-oci-kms.sh`); there's no automatic unlock and no provider fallback to `Local`. → [OCI KMS guide](oci-kms.md)

**Internal TLS.** Covered [above](#internal-api-tls) — every hop between Web, Caddy and the API is encrypted with a private CA, independent of the public-facing TLS terminated by Caddy/Cloudflare.

**Edge protection.** Caddy and CrowdSec sit in front of all five public hosts, behind Cloudflare. Only Cloudflare's published IP ranges are trusted for client-IP headers, a shared rate limiter caps requests per client across all hosts (with a stricter limit on Identity's authentication endpoints), and CrowdSec bans IPs that match flood or abuse patterns using only local detection — no data is shared with CrowdSec's community service. → [Edge security guide](edge-security.md)

**Authentication.** Every account requires TOTP-based two-factor authentication plus recovery codes. Public self-registration is disabled by default and can be opened, closed and capped to a maximum number of accounts from the Identity admin dashboard.

Together these mean a single leaked backup file, a stolen disk, or a flood of malicious requests each run into a layer built specifically to stop them, rather than one boundary doing all the work.

## Moving an existing deployment to the app subdomain

If you're moving from a single-domain deployment to the recommended `app.` subdomain topology:

1. Set `POCKETLEDGER_LANDING_DOMAIN` to the public domain and `POCKETLEDGER_WEB_DOMAIN` to the app subdomain.
2. Update the deployed Caddyfile from `Caddyfile.example`, add the new DNS record in Cloudflare, and confirm HTTPS works on both hosts (Cloudflare Full (strict) to the origin).
3. Keep both origins on the same site (e.g. `pocketledger.dev` and `app.pocketledger.dev`) so the existing `SameSite=Lax` app cookie still works for the landing page's session check.
4. Roll out Identity, Web, landing and the proxy configuration together.

On Identity startup, the existing Web OIDC client's login/logout redirect URIs are automatically synchronized with `OpenIddict:WebBaseUrl`; its client secret and permissions are preserved. Existing host-only browser cookies do **not** move to the new subdomain, so users may need to sign in again, and bookmarks to app paths on the old domain should be updated.

`Landing:AppBaseUrl` configures the landing page's app and registration links. Registration enters through Web so any existing BFF session is cleared before Identity creates a different account, and the completed onboarding flow establishes a matching Web session. Landing reads the registration state from Identity's anonymous `GET /Registration/Status` endpoint through `Identity:InternalBaseUrl` (the Docker network address `http://identity:5052/` in Compose), caches it for 30 seconds, and shows the CTA and the remaining free accounts only while registration is accepted. If Identity can't be reached, Landing shows registration as closed. `Landing:BaseUrl` on Web configures the profile-menu website link and the exact allowed CORS origin for `GET /Session/Status` — an endpoint that returns only an authentication boolean with no-store caching and shares no cookies or tokens with the landing page. Configure Cloudflare to bypass caching for `/Session/*` on the app domain, and don't apply public-page caching rules to authenticated app routes. When the app can't be reached, the landing page keeps a working login link.

## Blocking and deleting users

The `IsAccessBlocked` flag in the Identity database is the source of truth; nothing about blocking depends on Valkey. When an administrator blocks (or deletes) a user, Identity immediately:

1. sets the flag and rotates the security stamp, so sign-in and token issuance refuse the user and the Identity cookie, which is checked against the database on every request, stops working;
2. revokes all of the user's OpenIddict tokens and authorizations, so no refresh token can mint new access tokens;
3. adds the user to the API's `blocked_users` table — the API checks it on every authenticated request, so even an already issued, unexpired access token is rejected with `403`, whichever client sends it;
4. tells Web to terminate all of the user's sessions — the stored tickets and their tokens are wiped, and the next request from any of the user's browsers is redirected to the "access blocked" page.

Steps 3–4 go over the internal network (`Api__BaseUrl`, `Web__InternalBaseUrl`) authenticated with `POCKETLEDGER_ADMIN_OPERATIONS_KEY`. If either service is unreachable, the admin dashboard shows a warning, and a background job in Identity re-sends the full list of blocked and deleted users every 30 seconds until both are in sync. Deletion does not continue until steps 3–4 succeed. `Caddyfile.example` returns `404` for `/internal-admin/*` and `/api/v1/internal-admin/*` on the public hostnames; keep that rule in your deployed Caddyfile.

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

Production Compose configures all three applications with `RequestTelemetry__ConnectionString=valkey-security:6379`. For applications run directly on the host, start it with `docker compose -f compose.yaml -f compose.development.yaml up -d valkey-security` and set the connection string to `localhost:6380`; leaving it unset disables collection and makes queries return empty results. `RequestTelemetry__RetentionHours` defaults to and cannot exceed 48, while `RequestTelemetry__BucketMinutes` is fixed at 5 so later dashboard consumers can reuse the same bucket contract.

Telemetry and detection use their own Valkey instance (64 MB, `noeviction`, no persistence) instead of the financial cache's `allkeys-lru` instance. Eviction policies apply to the whole server, so with a shared instance cache pressure could silently evict telemetry buckets or incidents and the dashboard would show zero instead of missing data. With `noeviction` a full instance rejects writes instead: the failure is logged as a rate-limited warning and the stored data stays intact.

Recording is placed after authentication in every pipeline; in the API it also precedes user access blocking, so requests rejected for blocked users still appear in the statistics and in detection. Requests enqueue an in-memory observation and never wait for Valkey; a background worker coalesces observations before incrementing bucket counters. Buckets receive an absolute expiry at their start time plus the configured retention, so no bucket remains queryable after 48 hours. A full queue or a Valkey connection/write failure drops telemetry and logs a rate-limited warning without affecting the application request.

The API skips both request telemetry and suspicious request detection for requests whose validated access token was issued to the `pocketledger-web` client (`client_id`, or `azp` as a fallback). The Web BFF calls the API directly over the internal network without forwarding the browser's address, so every user's BFF traffic would otherwise arrive from the Web container's IP: detection would treat the combined load of all users as one high-volume client, and every page view would be counted once by the Web and again for each API call it makes. The Web already records those requests with the real client address and user ID, so dashboard counts represent requests received from outside the internal network. API requests authenticated by any other client, as well as unauthenticated or invalid-token requests, are still recorded.

### Suspicious request detection and stored IP addresses

The same `RequestTelemetry__ConnectionString` also enables suspicious request detection; no separate switch exists. Unlike the counters above, detection stores client IP addresses, which count as personal data:

- Every relevant request adds an entry to a per-IP sorted set (`pocketledger:suspicious-requests:v1:rolling:<sha256(ip)>`) that holds a rolling three-minute window. The key contains only a hash, but each entry stores the IP address, user ID, application name and host in base64 form, which is encoded but not anonymised. The set is capped at 201 entries per address, so a flood cannot grow it without bound.
- When an address exceeds 200 requests within that window, an incident hash is opened or extended with the plain IP address, the user IDs, the application names and the hosts observed, the request count, and the first and last detection times. The request count starts at the window count that triggered the incident and then increases with every further request while the incident stays active, meaning until the address has gone a full window without exceeding the threshold. Incidents expire 48 hours after their last update and appear in the **Suspicious request events** panel of the Identity admin dashboard.

A single background worker drains the in-memory queue (8192 observations) in batches of up to 1024, groups them by address and sends one script call per address, all pipelined over the multiplexer shared with request telemetry. If the queue still fills up, new observations are dropped without affecting requests.

The detection uses the address resolved by `UseForwardedHeaders`. Configure `ForwardedHeaders__KnownProxies` for the reverse proxy so detection sees the client's address instead of the proxy's. The landing privacy notice describes this retention; update it whenever the window, threshold or stored fields change.

Query intervals use an inclusive start and exclusive end. Inputs are expanded to whole five-minute UTC buckets, and the returned `FromUtc`/`ToUtc` expose those effective aligned boundaries. `AverageRequestsPerMinute` uses the selected user's count when a user filter is present, otherwise total volume, divided by the complete aligned interval in minutes. `IsAvailable` distinguishes a successful zero-count query from disabled configuration or a Valkey read failure.

## Backups

Encrypted `.plbackup` files contain the signed-in user's complete finance dataset — accounts, categories, transactions, recurring entries, debts, planner history — but never account passwords, TOTP secrets, recovery codes, authentication audit events, or BFF session tokens. Restoring one is independent of which encryption-key provider (local certificate or OCI KMS) is active; see the [database encryption guide](database-encryption.md#key-rotation-and-recovery) for key-ring backup and recovery, which is a separate concern from finance data backup.

## Volumes

The three named database volumes are `web-postgres-data`, `api-postgres-data`, and `identity-postgres-data`. `docker compose down` preserves them; `docker compose down --volumes` permanently deletes all three databases. Caddy and CrowdSec have their own volumes (`caddy-data`, `caddy-config`, `caddy-logs`, `crowdsec-config`, `crowdsec-data`) — see [Edge security](edge-security.md#versions-and-storage) for what each one holds and how it should be protected.
