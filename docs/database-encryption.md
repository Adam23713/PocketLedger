# Database encryption

## Scope and guarantees

PocketLedger encrypts selected fields before EF Core writes them to PostgreSQL, in two layers. User data (names, notes, planner snapshots) is first encrypted with a key that belongs to that user and that only the user's password or recovery key can open — see [Per-user data keys](#per-user-data-keys). Every encrypted field, user data included, then gets the host layer: all three hosts use separate ASP.NET Core Data Protection key rings, persisted outside PostgreSQL. Production requires an existing key directory and an RSA private certificate; the key ring is encrypted with that certificate. The PostgreSQL containers never mount keys or certificates. There is no production switch to disable this protection and no plaintext fallback on decryption errors.

Field encryption alone does **not** encrypt the whole database. Amounts, dates, relationships, authentication lookup fields and database metadata remain readable in a database copy, and whoever obtains the whole VPS disk still obtains the ciphertext alongside it. A separate directory or Docker bind mount is not protection against whole-disk theft; a copy of the disk and a copy of the key material together are enough to decrypt it.

Full-disk encryption (for example LUKS) is one way to close that remaining gap, by encrypting the offline block device itself — PostgreSQL's tables, indexes, WAL and temporary files included — so a stolen or copied disk is unreadable without a separate passphrase. PocketLedger doesn't ship or test a LUKS deployment path; if whole-disk encryption matters for your VPS, plan and test it independently for your provider and storage layout. This deployment instead keeps the *key* side of that risk out of reach of a disk copy by moving key custody off the VPS entirely, into an OCI Vault/KMS hardware module with encrypted (`.enc`) credential files — see the [OCI KMS guide](oci-kms.md). That protects the keys that unlock the encrypted fields; it is a different guarantee from encrypting the disk itself, and the two are complementary rather than substitutes for each other.

CSV files accepted for transaction import are plaintext. Transaction exports are password-protected `.xlsx` workbooks, while complete finance backup/restore uses the password-protected `.plbackup` format. Database encryption and exported-file encryption are independent protections.

## Per-user data keys

The host layer protects data at rest from anyone who obtains the database, a disk copy or a backup, but anyone who controls the running host — including the operator — can use the host keys to read every user's data. The per-user layer closes that gap for the user's sensitive text: the operator cannot read it passively, even with the database, the key rings and an unlocked OCI KMS.

### Keys

| Key | Where it lives | Persisted |
| --- | --- | --- |
| Password, or the data password for external-login users | The user's memory | Never (Identity keeps only its usual one-way password hash) |
| Recovery key (30 Crockford base32 characters, 150 bits) | Saved offline by the user; shown once | Never |
| DEK: the user's 256-bit data key | Identity `UserDataKeys`, only wrapped: AES-256-GCM under an Argon2id key (64 MiB, 3 iterations) from the password and, separately, from the recovery key; both wraps also carry the Identity host layer | Wrapped only |
| T: pending sign-in key | The 15-minute `PocketLedger.Identity.SignInKey` cookie; Identity memory holds only AES-GCM(T, DEK) | Never |
| SK: session key | The `PocketLedger.Web.DataKey` browser-session cookie (HttpOnly, Secure, SameSite=Lax) | Never |
| W = AES-GCM(SK, DEK) | The Web BFF's server-side session ticket | Yes, useless without SK |

Every wrap binds the user ID, and session wraps also bind the session ID (`pl_sid` access-token claim), as AES-GCM associated data, so wrapped keys and ciphertext cannot be moved to another user, field group or session.

### Flows

- **Registration, external sign-up and `bootstrap-identity`** create the DEK in the same transaction as the account and show the recovery key once. `bootstrap-identity` prints it to the console.
- **Sign-in.** The password step (or, for external providers, the data password step) opens the DEK and keeps it pending until sign-in completes, through TOTP or authenticator setup. TOTP and recovery-code sign-in restart when no pending key exists. Wrong data passwords count toward the normal lockout.
- **Authorization.** `/connect/authorize` requires the pending key, so a client session always starts from a fresh password entry; with only an Identity cookie, the user re-enters the password on **Unlock your data** (TOTP is not asked again). Identity prepares SK and W in memory and puts only a handoff ID and `pl_sid` into the authorization code. The code exchange returns SK and W once, as `pl_session_key` and `pl_wrapped_data_key` token-response parameters. Refreshed tokens keep `pl_sid`; tokens without it are refused.
- **Requests.** The Web BFF stores W in its ticket and SK in the browser cookie, removes both from the token response, and sends them to the API as `X-PocketLedger-Session-Key` and `X-PocketLedger-Wrapped-Data-Key`. The API removes the headers before anything else runs, unwraps the DEK for the token's user and session, and keeps it only for that request. A missing half, an invalid key or an API 401 makes the Web BFF start a new sign-in.
- **Password change** re-wraps the DEK; no data is re-encrypted. For external-login users it changes the data password.
- **Forgotten password.** **Recover account** opens the DEK with the recovery key, sets a new password (or data password) and issues a new recovery key, because the old one was typed into a browser. Signing in afterwards still needs the authenticator. **Without the password and the recovery key, nobody — including the administrator — can recover the user's data.** The Security page can replace a lost recovery key while the password is known.

### What runs without the user

Background work has no user key. The recurring-transaction worker reads through a cross-tenant context that exposes only the per-user ciphertext: all notes share one field group, so it copies a template's note into the new transaction without reading it. It skips the planner refresh. There is no planner month worker: months that ended close on the user's next planner visit or write, in the user's time zone, from the last saved snapshot. Recurring transactions created while the user is away are in the ledger and current balances, but a month that ends before the user returns keeps the snapshot from the user's last activity in it. Financial cache entries are sealed with the user's key, and requests without a key bypass the cache.

### Guarantees and limits

| Situation | Per-user data (names, notes, planner) |
| --- | --- |
| Database dump, disk copy, snapshot or backup | Unreadable without the host keys and, per user, the password or recovery key |
| Operator with the database, key rings and an unlocked OCI KMS | Unreadable for users who are not signed in, and between requests |
| Root on the running host during a user's request | Readable for that user, by capturing process memory, headers or the password at sign-in |
| Operator deploying modified code | Readable for users who sign in afterwards. Only client-side end-to-end encryption would prevent this |
| Amounts, dates, types, IDs, relationships, usernames, emails | Not per-user encrypted (host layer and storage only), so SQL filters, joins and balances keep working |

Identity's pending keys and session handoffs live in process memory, which assumes a single Identity instance (as in the supplied Compose files). The API's Swagger UI in Development cannot send SK and W, so its calls that touch user data return `401 data_key_required`.

## Data classification

| Data | Protection and reason |
| --- | --- |
| `Transaction.Note` (the ticket's `Description`) | Per-user and host encryption. User-controlled sensitive text. |
| `RecurringTransaction.Note`, `PlannerItem.Note`, `Debt.Note` | Per-user and host encryption, in one field group so notes can be copied between them without being read. |
| `Account.Name`, `Category.Name` | Per-user and host encryption; names can reveal personal information. Sort after decryption. |
| `Debt.Name`, `Debt.CounterpartyName` | Per-user and host encryption. |
| `UserPreference.DisplayName` | Per-user and host encryption. |
| `PlannerMonthRecord.SnapshotJson`, `OpeningBalancesJson` | Per-user and host encryption of the complete persisted payload. Snapshots include names, notes, monthly Notes and financial history. No SQL JSON query depends on these payloads. |
| Amounts, initial balances, currency, transaction date/time/type, IDs, owner IDs, relationships | Storage encryption. Keeping typed, indexed columns preserves filters, joins, balance calculations and reporting. Randomized field encryption would prevent useful SQL comparisons/aggregations and require loading much more data. |
| Icons, colors, display ordering, flags, timezone and currency-format preferences | Storage encryption; low-sensitivity presentation/configuration data. |
| Identity `UserDataKeys` wraps | The DEK wrapped by the password and by the recovery key, under host encryption. |
| Identity `AspNetUserTokens.Value` | Host encryption, including the existing Identity authenticator key and recovery-code representation. Identity's token generation/validation stays unchanged. |
| Identity phone number, last successful login IP, audit remote/forwarded IP, user agent, metadata | Host encryption. Identity needs these without the user's key. |
| Identity usernames, normalized usernames/emails, email, user IDs; audit username, timestamp, outcome, request path, correlation/session fingerprints | Storage encryption. Username/email lookup and indexed security-event queries remain compatible. Encrypting only one copy while leaving the normalized lookup copy would not hide the identity. No blind-index scheme is introduced. |
| Password hashes, Identity/OpenIddict client-secret hashes | Existing one-way framework hashing plus storage encryption. Never replaced with reversible password encryption. |
| Web BFF session including access/refresh tokens | Existing Data Protection encryption; keys now live outside its database and are certificate-protected. |
| OpenIddict tokens/authorizations/application configuration | Existing framework handling plus storage encryption. Protocol signing/encryption behavior is unchanged. |
| OIDC signing key, client secret, database password and CrowdSec key in deployment configuration | Outside the database; protect the deployment environment and its backups with host permissions/storage encryption. Never commit production values. |
| Cloud credentials, external financial integration tokens, backup passwords, separate goal entity | No corresponding implemented storage identified in this version. Classify and protect them when introduced. |
| Financial cache payloads | Per-user encryption under a host layer with a user/operation-specific purpose. Valkey still has persistence disabled. Cache-key dimensions are hashed; metadata remains visible. Old cache entries expire under the previous namespace. |

Host swap, core dumps, container logs, reverse-proxy logs and provider snapshots are separate persistent surfaces. For the complete storage guarantee, keep swap disabled or encrypted, avoid application core dumps, and apply appropriate encryption/retention to logs and snapshots. Whatever full-disk encryption approach you add, remember it only protects what sits on the encrypted device — it doesn't by itself extend to these other surfaces.

## Application design

`PocketLedger.Security` shares framework key configuration and field converters between Infrastructure, Web and Identity, without making the hosts depend on finance persistence. EF converters protect writes and decrypt materialized fields/projections. User fields use `ConfigureUserField`: AES-256-GCM with the request's DEK (`UserKeyContext`), then Data Protection under `PocketLedger.Database.v2`; reading or writing them without an unlocked key throws, and the API answers `401 data_key_required`. The cross-tenant background worker receives the same provider as the API. EF model cache entries are separated by provider instance. Existing standalone contexts without a registered encryption provider remain available to tooling; deployed hosts always register and require encryption configuration.

The protected payload is the standard Data Protection format, with a stable application discriminator and `PocketLedger.Database.v1` plus a per-field purpose. Do not rename these identifiers when moving deployments. Data Protection is not a turnkey archival system: using it for long-lived database values requires retaining **all** historical key-ring files and their wrapping certificates for as long as any database/backup needs them. Automatic expiry does not mean a key can be deleted.

Notes search first applies owner/date/account/category/type/amount filters in SQL, streams only candidate IDs/notes, decrypts and compares with `OrdinalIgnoreCase`, then applies matching IDs to paging, counts, daily totals and exports. `%`, `_` and backslash are literal characters. This preserves case-insensitive substring search, but Unicode case equivalence can differ from a PostgreSQL locale-specific `ILIKE`. A broad search remains O(candidate notes), with O(matches) IDs in memory; there is no plaintext search index. Account/category sorting occurs after materialization, using the same .NET comparer already used by other views.

## Stage 1: field encryption on the existing VPS storage

Prepare a **new installation/database set**. The schema migrations intentionally reject populated legacy databases; they do not encrypt existing rows in place. This also applies to per-user data keys: the `AddUserDataKeys` (Identity) and `RequirePerUserDataKeys` (finance) migrations refuse populated databases, because existing users would have no data key. This follows the agreed export/recreate/restore workflow. Never run the old application against an encrypted database, and do not downgrade its schema with populated tables.

1. Export an encrypted `.plbackup` using the existing UI and keep its password separately. It excludes Identity users, TOTP, recovery codes and Web sessions. Verify that it is the expected dataset before retiring anything.
2. Prepare a fresh directory, outside the repository and outside database volumes:

   ```bash
   sudo bash tools/initialize-encryption-keys.sh /opt/pocketledger-security
   ```

   The script refuses existing directories. It creates separate `api`, `web`, `identity` key/certificate directories and unpassworded `active.pfx` files protected by host permissions. Root execution assigns the .NET image UID 1654. If using a custom container UID, set ownership accordingly. The PFX itself is sensitive key material; an empty PFX password is intentional because unattended container restarts use filesystem access control. If you additionally encrypt the underlying disk (see [Scope and guarantees](#scope-and-guarantees)), that protects this file offline too.
3. Set `POCKETLEDGER_SECURITY_DIRECTORY=/opt/pocketledger-security` in the deployment `.env`. Compose mounts only each host's own directories. Mount sources must exist; Compose will not create them automatically.
4. Stop the old deployment in its existing Compose project. Set `COMPOSE_PROJECT_NAME=pocketledger-encrypted` in the new deployment's `.env` and keep it unchanged afterwards. The different project name gives the new deployment three empty named PostgreSQL volumes. Do not run the two deployments concurrently on the same public ports.

   ```bash
   docker compose build
   docker compose up -d identity-database
   docker compose run --rm identity bootstrap-identity
   docker compose up -d
   ```

5. Save the data recovery key that `bootstrap-identity` prints, create the new Identity/TOTP setup, save the new recovery codes, and restore the `.plbackup` through the UI. Compare transaction counts, accounts, balances, planner history and notes with the old export. The imported fields are encrypted automatically on persistence.
6. Back up the new key rings **after first use**, alongside a separately secured copy of their private certificates. A finance `.plbackup` is not a key-ring backup. Retain the old deployment only until recovery is verified, then deliberately retire its plaintext volumes and provider snapshots. Deleting a Docker volume does not guarantee secure erasure on SSDs or in provider backups.

If OCI KMS will protect the new installation, stop here until the Local deployment is fully healthy and its fresh databases, Identity account, TOTP and restored finance data have been verified. Then follow the Local-to-OCI activation procedure in `docs/oci-kms.md`. Do not run the OCI key-ring migration as part of the legacy database export/recreate/restore transition.

The migration guard also refuses an old populated Web database, rather than silently deleting the old session key ring. A new Web database is part of this reset. Rollback uses the old application with its old database or a fresh old-version database restored from the finance export; never point it at the new encrypted database.

## Key rotation and recovery

- A user's DEK is not rotated. Password changes and account recovery only re-wrap it; the recovery key can be replaced from the Security page. Every user-encrypted value records the DEK version, so a later rotation can be added without guessing.
- Lost password and lost recovery key means the user's per-user data is gone. Delete the account; there is no administrative reset that keeps the data.

- Framework data keys rotate automatically (default lifetime: 90 days). Retain expired keys indefinitely while data or backups reference them. Never delete or revoke old keys as routine rotation; old database values are not automatically re-encrypted.
- To rotate wrapping certificates, generate a new RSA certificate with the initialization helper in a new staging directory, retain the previous PFX, then deploy the new `active.pfx`. Configure `Encryption__PreviousCertificatePaths__0=/run/pocketledger-certificates/previous.pfx` (and further indices) on the affected host. Keep all old wrapping private keys needed by existing key-ring XML files. Rotation affects newly generated framework keys; it does not rewrap historical XML or immediately re-encrypt rows.
- For compromise recovery, stop access, restore/re-encrypt through a separately planned maintenance procedure and rotate affected protocol credentials as appropriate. Routine certificate replacement alone does not remediate stolen data keys. A fresh database restored from an encrypted `.plbackup` under new key rings can re-encrypt finance data; Identity still needs its own recovery plan.
- To recover a database backup, restore the correct host key-ring directories, all required certificate private keys, permissions, application names and database together. Confirm recovery in an isolated deployment before retiring originals. Store key backups separately from database backups and protect them with an offline secret.
- Missing configuration, missing certificates, inaccessible keys or invalid ciphertext must fail; do not replace encrypted values with empty text. A newly generated key ring cannot decrypt old rows. Restore the historical key ring instead.
- Development uses a persistent `.local/encryption` directory per host, with no wrapping certificate required. These directories are ignored by Git and excluded from Docker build context. They are not suitable production key storage. Losing them loses access to that development database's encrypted fields too.

## Validation and performance

Run the existing build/test suite and the synthetic crypto benchmark:

```bash
dotnet build PocketLedger.slnx -m:1
dotnet test PocketLedger.slnx --no-build -m:1
dotnet run --project tools/PocketLedger.EncryptionBenchmark -c Release
```

A local .NET 10.0.12 Linux measurement (10,000 operations, warmed provider, 20 logical CPUs reported) produced the following elapsed times. These are indicative single-run numbers, not deployment SLAs:

| Synthetic text | Protect | Unprotect | Decrypt + substring search | Stored chars |
| --- | ---: | ---: | ---: | ---: |
| 100 accented characters (200 UTF-8 bytes) | 210 ms | 184 ms | 227 ms | 390 |
| 500 accented characters (1,000 UTF-8 bytes) | 323 ms | 383 ms | 409 ms | 1,456 |
| 10,000 accented characters (20,000 UTF-8 bytes) | 1,731 ms | 1,288 ms | 906 ms | 26,800 |

The benchmark reports encryption/decryption/search CPU time, allocations and ciphertext expansion. It uses only synthetic data and a disposable certificate/key ring; it does not connect to an application database. Database I/O, EF materialization and wide-search cost on the real dataset need deployment measurements. The PostgreSQL/Valkey integration suite requires its existing test endpoints; ordinary in-memory tests do not prove database ciphertext.

Implementation validation used an isolated local PostgreSQL 18 instance (the supplied production image remains PostgreSQL 17). All three schemas migrated successfully. Manual API writes/readback confirmed encrypted account names, transaction notes and planner snapshots; accented and literal-wildcard searches, totals/balance, the backup serialization and restore flow available at that time, and readback after API restart succeeded. Identity bootstrap and authenticator-key reset produced encrypted `AspNetUserTokens.Value`. Key-ring XML contained certificate-encrypted secrets. This is not a measurement or verification of the actual VPS or PostgreSQL 17 container.

Before retiring the old deployment, verify authorized reads after restart, encrypted backup restore, notes search (including accents and literal `%`/`_`), counts and paging, daily totals, balances, recurring transactions, planner Notes/history, TOTP/recovery and a full key/database restore. Inspect raw PostgreSQL columns in the isolated installation to confirm ciphertext.

## References

- [PostgreSQL 17 encryption options](https://www.postgresql.org/docs/17/encryption-options.html)
- [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [Data Protection key management and retention](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0)
