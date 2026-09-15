# Database encryption (PL-93)

## Scope and guarantees

PocketLedger now encrypts selected fields before EF Core writes them to PostgreSQL. All three hosts use separate ASP.NET Core Data Protection key rings, persisted outside PostgreSQL. Production requires an existing key directory and an RSA private certificate; the key ring is encrypted with that certificate. The PostgreSQL containers never mount keys or certificates. There is no production switch to disable this protection and no plaintext fallback on decryption errors.

Field encryption alone does **not** encrypt the whole database. Amounts, dates, relationships, authentication lookup fields and database metadata remain readable in a database copy. Until the LUKS stage below is completed, someone obtaining the whole VPS disk may obtain both ciphertext and the certificate private keys. A separate directory or Docker bind mount is not protection against whole-disk theft.

LUKS protects the offline block device, including PostgreSQL tables, indexes, WAL and temporary database files stored on that filesystem. Copying files through an already unlocked filesystem, SQL access with valid credentials, root access to a running VPS, memory access and malicious database writes are outside this offline-theft guarantee. Host/provider snapshots of plaintext disks or running VM memory need separate handling. The field protection purpose separates fields and applications; it does not bind ciphertext to a specific row or prevent replay of an older value.

CSV files accepted for transaction import are plaintext. Transaction exports are password-protected `.xlsx` workbooks, while complete finance backup/restore uses the password-protected `.plbackup` format. Database encryption and exported-file encryption are independent protections.

## Data classification

| Data | Protection and reason |
| --- | --- |
| `Transaction.Note` (the ticket's `Description`) | Application encryption. User-controlled sensitive text. |
| `RecurringTransaction.Note`, `PlannerItem.Note` | Application encryption; these can copy or generate transaction notes. |
| `Account.Name`, `Category.Name` | Application encryption; names can reveal personal information. Sort after decryption. |
| `Debt.Name`, `Debt.CounterpartyName`, `Debt.Note` | Application encryption. |
| `UserPreference.DisplayName` | Application encryption. |
| `PlannerMonthRecord.SnapshotJson`, `OpeningBalancesJson` | Encrypt the complete persisted payload. Snapshots include names, notes, monthly Notes and financial history. No SQL JSON query depends on these payloads. |
| Amounts, initial balances, currency, transaction date/time/type, IDs, owner IDs, relationships | Storage encryption. Keeping typed, indexed columns preserves filters, joins, balance calculations and reporting. Randomized field encryption would prevent useful SQL comparisons/aggregations and require loading much more data. |
| Icons, colors, display ordering, flags, timezone and currency-format preferences | Storage encryption; low-sensitivity presentation/configuration data. |
| Identity `AspNetUserTokens.Value` | Application encryption, including the existing Identity authenticator key and recovery-code representation. Identity's token generation/validation stays unchanged. |
| Identity phone number, last successful login IP, audit remote/forwarded IP, user agent, metadata | Application encryption. |
| Identity usernames, normalized usernames/emails, email, user IDs; audit username, timestamp, outcome, request path, correlation/session fingerprints | Storage encryption. Username/email lookup and indexed security-event queries remain compatible. Encrypting only one copy while leaving the normalized lookup copy would not hide the identity. No blind-index scheme is introduced. |
| Password hashes, Identity/OpenIddict client-secret hashes | Existing one-way framework hashing plus storage encryption. Never replaced with reversible password encryption. |
| Web BFF session including access/refresh tokens | Existing Data Protection encryption; keys now live outside its database and are certificate-protected. |
| OpenIddict tokens/authorizations/application configuration | Existing framework handling plus storage encryption. Protocol signing/encryption behavior is unchanged. |
| OIDC signing key, client secret, database password and CrowdSec key in deployment configuration | Outside the database; protect the deployment environment and its backups with host permissions/storage encryption. Never commit production values. |
| Cloud credentials, external financial integration tokens, backup passwords, separate goal entity | No corresponding implemented storage identified in this version. Classify and protect them when introduced. |
| Financial cache payloads | Application encryption with a user/operation-specific purpose. Valkey still has persistence disabled. Cache-key dimensions are hashed; metadata remains visible. Old cache entries expire under the previous namespace. |

Host swap, core dumps, container logs, reverse-proxy logs and provider snapshots are separate persistent surfaces. For the complete storage guarantee, keep swap disabled or encrypted, avoid application core dumps, and apply appropriate encryption/retention to logs and snapshots. The supplied Compose override only moves the three PostgreSQL data directories; it does not configure the entire VPS filesystem.

## Application design

`PocketLedger.Security` shares framework key configuration and field converters between Infrastructure, Web and Identity, without making the hosts depend on finance persistence. EF converters protect writes and decrypt materialized fields/projections. The cross-tenant background worker receives the same provider as the API. EF model cache entries are separated by provider instance. Existing standalone contexts without a registered encryption provider remain available to tooling; deployed hosts always register and require encryption configuration.

The protected payload is the standard Data Protection format, with a stable application discriminator and `PocketLedger.Database.v1` plus a per-field purpose. Do not rename these identifiers when moving deployments. Data Protection is not a turnkey archival system: using it for long-lived database values requires retaining **all** historical key-ring files and their wrapping certificates for as long as any database/backup needs them. Automatic expiry does not mean a key can be deleted.

Notes search first applies owner/date/account/category/type/amount filters in SQL, streams only candidate IDs/notes, decrypts and compares with `OrdinalIgnoreCase`, then applies matching IDs to paging, counts, daily totals and exports. `%`, `_` and backslash are literal characters. This preserves case-insensitive substring search, but Unicode case equivalence can differ from a PostgreSQL locale-specific `ILIKE`. A broad search remains O(candidate notes), with O(matches) IDs in memory; there is no plaintext search index. Account/category sorting occurs after materialization, using the same .NET comparer already used by other views.

## Stage 1: field encryption on the existing VPS storage

Prepare a **new installation/database set**. The schema migrations intentionally reject populated legacy databases; they do not encrypt existing rows in place. This follows the agreed export/recreate/restore workflow. Never run the old application against an encrypted database, and do not downgrade its schema with populated tables.

1. Export an encrypted `.plbackup` using the existing UI and keep its password separately. It excludes Identity users, TOTP, recovery codes and Web sessions. Verify that it is the expected dataset before retiring anything.
2. Prepare a fresh directory, outside the repository and outside database volumes:

   ```bash
   sudo bash tools/initialize-encryption-keys.sh /opt/pocketledger-security
   ```

   The script refuses existing directories. It creates separate `api`, `web`, `identity` key/certificate directories and unpassworded `active.pfx` files protected by host permissions. Root execution assigns the .NET image UID 1654. If using a custom container UID, set ownership accordingly. The PFX itself is sensitive key material; an empty PFX password is intentional because unattended container restarts use filesystem access control. LUKS later protects that file offline.
3. Set `POCKETLEDGER_SECURITY_DIRECTORY=/opt/pocketledger-security` in the deployment `.env`. Compose mounts only each host's own directories. Mount sources must exist; Compose will not create them automatically.
4. Stop the old deployment in its existing Compose project. Start the new version under a **different Compose project name**, for example `pocketledger-encrypted`, so its three named PostgreSQL volumes start empty. Do not run the two deployments concurrently on the same public ports.

   ```bash
   docker compose -p pocketledger-encrypted build
   docker compose -p pocketledger-encrypted up -d identity-database
   docker compose -p pocketledger-encrypted run --rm identity bootstrap-identity
   docker compose -p pocketledger-encrypted up -d
   ```

5. Create the new Identity/TOTP setup, save the new recovery codes, and restore the `.plbackup` through the UI. Compare transaction counts, accounts, balances, planner history and notes with the old export. The imported fields are encrypted automatically on persistence.
6. Back up the new key rings **after first use**, alongside a separately secured copy of their private certificates. A finance `.plbackup` is not a key-ring backup. Retain the old deployment only until recovery is verified, then deliberately retire its plaintext volumes and provider snapshots. Deleting a Docker volume does not guarantee secure erasure on SSDs or in provider backups.

The migration guard also refuses an old populated Web database, rather than silently deleting the old session key ring. A new Web database is part of this reset. Rollback uses the old application with its old database or a fresh old-version database restored from the finance export; never point it at the new encrypted database.

## Stage 2: manually unlocked LUKS storage on Ubuntu 22.04 LTS

This stage requires an identified, dedicated block device or an independently planned storage migration. **Do not run a format command against an existing root/data partition.** The repository does not choose or format a device automatically.

Provision a LUKS2 device with `cryptsetup`, keep the passphrase off the VPS disk, and back up the LUKS header separately. Unlock it as `/dev/mapper/pocketledger-data`, create an ext4 filesystem on the new mapping, and mount it at `/srv/pocketledger`. These device-specific provisioning steps must be adapted to the VPS layout. There is no automatic-unlock entry or passphrase key file supplied.

After mounting, create `/srv/pocketledger/databases/{api,web,identity}` and `/srv/pocketledger/security`. If Stage 1 already contains data, stop all application and database processes before moving anything; copy the complete PostgreSQL directories while stopped, preserving ownership/modes, and copy the **existing** security directory. Do not generate replacement keys. PostgreSQL must remain on the same major version and the original data directories must stay available for rollback until validation succeeds. Alternatively, initialize fresh databases on the new mount and restore an encrypted finance backup, recreating Identity again.

Use the wrapper for every encrypted-storage Compose operation, preserving the chosen project name:

```bash
sudo bash tools/compose-encrypted-storage.sh -p pocketledger-encrypted config --quiet
sudo bash tools/compose-encrypted-storage.sh -p pocketledger-encrypted up -d
```

The wrapper checks the mount, its mapper device and LUKS2 status, forces the security directory onto that mount, then uses `compose.encrypted-storage.yaml`. The override replaces all three database mounts with bind mounts on LUKS and disables Docker restart policies for the three applications and databases. This deliberately requires a manual start after reboot. Do not use the base Compose file alone once you switch to this stage, and do not bypass the wrapper. A custom Docker/systemd autostart must obey the same mount checks.

After a VPS reboot, manually unlock the device, mount `/srv/pocketledger`, then run the wrapper's `up -d`. Before closing the mapping, run the wrapper's `down`, unmount the filesystem, and close the mapping. An ordinary application-container restart while the filesystem remains mounted needs no new passphrase.

A raw offline copy of this locked block device is encrypted. A `pg_dump`, plaintext application export, tar copy from the mounted directory or snapshot created from plaintext before the transition is **not** made encrypted by this change.

## Key rotation and recovery

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

The benchmark reports encryption/decryption/search CPU time, allocations and ciphertext expansion. It uses only synthetic data and a disposable certificate/key ring; it does not connect to an application database. Database I/O, EF materialization, wide-search cost on the real dataset, and LUKS overhead need deployment measurements. The PostgreSQL/Valkey integration suite requires its existing test endpoints; ordinary in-memory tests do not prove database ciphertext or LUKS protection.

Implementation validation used an isolated local PostgreSQL 18 instance (the supplied production image remains PostgreSQL 17). All three schemas migrated successfully. Manual API writes/readback confirmed encrypted account names, transaction notes and planner snapshots; accented and literal-wildcard searches, totals/balance, the backup serialization and restore flow available at that time, and readback after API restart succeeded. Identity bootstrap and authenticator-key reset produced encrypted `AspNetUserTokens.Value`. Key-ring XML contained certificate-encrypted secrets. The LUKS wrapper refused to run without its mount. This is not a measurement or verification of the actual VPS or PostgreSQL 17 container.

Before retiring the old deployment, verify authorized reads after restart, encrypted backup restore, notes search (including accents and literal `%`/`_`), counts and paging, daily totals, balances, recurring transactions, planner Notes/history, TOTP/recovery and a full key/database restore. Inspect raw PostgreSQL columns in the isolated installation to confirm ciphertext, and confirm startup refuses an unmounted LUKS filesystem.

## References

- [PostgreSQL 17 encryption options](https://www.postgresql.org/docs/17/encryption-options.html)
- [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [Data Protection key management and retention](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0)
