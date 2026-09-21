# OCI Vault/KMS encrypted credentials and manual unlock (PL-96, PL-99)

PocketLedger has three independent ASP.NET Core Data Protection key rings: API, Web and Identity. Each ring may contain many automatically rotated Data Protection master keys. Each service uses its own non-exportable OCI HSM AES-256 KEK, dedicated OCI IAM user and encrypted API signing credential. Automatic Data Protection key generation and rotation remain enabled.

There is no provider fallback. `Encryption:KeyProvider=OciVault` never falls back to `Local`. OCI receives only Data Protection key XML for wrap/unwrap; application records remain encrypted locally.

## Security boundary

Persistent VPS storage contains Data Protection key rings, normal configuration and three `*.enc` credential files, but not a plaintext OCI config, signing PEM or passphrase. A powered-off disk, VM snapshot, filesystem copy, database backup and these files are insufficient without the administrator's unlock passphrase.

After unlock, the OCI signing key representation and Data Protection keys can remain in process memory because later key rotation or historical-key loading may require OCI. Temporary byte/character buffers are cleared where practical, but .NET, Bouncy Castle and the OCI SDK can create managed copies that cannot be guaranteed to be zeroized. Root/kernel compromise of an already-unlocked process can expose runtime secrets and is outside PL-99's complete protection boundary.

## Encrypted credential format

The binary `PLOCKMS1` format contains an explicit format version, KDF/cipher identifiers, Argon2id parameters, ciphertext length, a random 16-byte salt, random 12-byte nonce, AES-256-GCM ciphertext and 16-byte authentication tag. The authenticated header makes parameter manipulation fail closed. Current Argon2id parameters are 64 MiB, three iterations and one lane; accepted bounds prevent attacker-controlled resource exhaustion during parsing.

The encrypted payload contains tenancy OCID, user OCID, fingerprint, region and a PKCS#8 signing key. The entire payload is protected by AES-256-GCM. Passwords are never used directly as AES keys. `Konscious.Security.Cryptography.Argon2` is used because it is a focused, established .NET Argon2id implementation; framework `AesGcm` provides authenticated encryption.

## OCI resources and least privilege

Create `pocketledger-api-kek`, `pocketledger-web-kek` and `pocketledger-identity-kek`, plus three dedicated IAM users/groups. Grant each group only `KEY_ENCRYPT` and `KEY_DECRYPT` for its own Key OCID:

```text
Allow group PocketLedgerApiKmsClients to use keys in compartment id <COMPARTMENT_OCID> where all {target.key.id = '<API_KEY_OCID>', request.permission = 'KEY_ENCRYPT'}
Allow group PocketLedgerApiKmsClients to use keys in compartment id <COMPARTMENT_OCID> where all {target.key.id = '<API_KEY_OCID>', request.permission = 'KEY_DECRYPT'}
```

Repeat for Web and Identity with their own groups and keys. Do not grant key management, export or access to another service's key.

## Supported activation path

The supported production path starts with a complete, healthy deployment using the default `Local` key provider and fresh databases. OCI KMS is activated only after all three applications have created and successfully used their certificate-protected Data Protection key rings.

Do not combine the legacy-database export/recreate/restore transition with the Local-to-OCI key-ring migration. Complete the fresh Local deployment first, including Identity bootstrap, login, TOTP setup and any finance backup restore. Then follow the activation procedure below.

Set a stable Compose project name in `.env` before the first deployment:

```text
COMPOSE_PROJECT_NAME=pocketledger
```

Never change this value merely because the repository is moved or checked out into a differently named directory. Compose uses the project name to select its containers, networks and named database volumes. Before continuing, the normal Local deployment must satisfy all of the following:

- `docker compose config --quiet` succeeds;
- API, Web and Identity use `Encryption__KeyProvider=Local`;
- the three databases are fresh and fully migrated;
- Identity bootstrap and login succeed;
- `${POCKETLEDGER_SECURITY_DIRECTORY}/{api,web,identity}/keys` each contain at least one `key-*.xml` file;
- each application still has its own `certificates/active.pfx`;
- a backup of the databases and the complete security directory has been verified.

For a brand-new installation, establish that Local baseline first:

1. Copy `.env.example` to `.env`, keep `COMPOSE_PROJECT_NAME=pocketledger`, replace every placeholder and copy `Caddyfile.example` to `Caddyfile`.
2. Create the Local Data Protection certificates/key directories and the internal TLS material. Both helpers refuse to overwrite existing material:

   ```bash
   sudo bash tools/initialize-encryption-keys.sh /opt/pocketledger-security
   sudo bash tools/initialize-internal-tls.sh /opt/pocketledger-security
   ```

3. Validate and build the Local configuration, initialize Identity, then start the complete deployment:

   ```bash
   docker compose config --quiet
   docker compose build
   docker compose up -d identity-database
   docker compose run --rm identity bootstrap-identity
   docker compose up -d
   ```

4. Log in, configure TOTP and save the recovery codes. If this is a legacy-data transition, restore and validate the encrypted finance backup now. Do not add an OCI Compose override yet.
5. Confirm that all three services are healthy and have created their own Local key-ring XML. Back up the databases and the complete `/opt/pocketledger-security` directory. This is the starting point for OCI activation.

## Create encrypted OCI credentials

Run the administrative CLI on a trusted workstation with the .NET 10 SDK. Repeat for the API, Web and Identity PEM files:

```bash
dotnet run --project tools/PocketLedger.Security.Cli -- create-oci-credential
```

The CLI interactively asks for the passphrase-protected PEM path, output path, OCI identifiers, PEM passphrase and a new credential passphrase. Both passphrases use a no-echo terminal and are never command arguments or environment variables. It imports the key in memory, verifies the public-key fingerprint, writes no temporary plaintext key, and creates the output atomically with mode `0600`.

Transfer only these files to the VPS:

```text
${POCKETLEDGER_SECURITY_DIRECTORY}/api/oci/api.enc
${POCKETLEDGER_SECURITY_DIRECTORY}/web/oci/web.enc
${POCKETLEDGER_SECURITY_DIRECTORY}/identity/oci/identity.enc
```

Each `oci` directory should be mode `0700`; each file mode `0600`; both must be readable by container UID/GID 1654. Do not leave the source PEM or OCI config on the VPS. `.gitignore` excludes `*.pem` and `*.enc`, but permissions and deployment exclusions remain mandatory.

For example, create the destination directories before copying the files and then enforce the expected ownership and modes:

```bash
sudo install -d -m 0700 -o 1654 -g 1654 \
  /opt/pocketledger-security/api/oci \
  /opt/pocketledger-security/web/oci \
  /opt/pocketledger-security/identity/oci
sudo install -m 0600 -o 1654 -g 1654 api.enc /opt/pocketledger-security/api/oci/api.enc
sudo install -m 0600 -o 1654 -g 1654 web.enc /opt/pocketledger-security/web/oci/web.enc
sudo install -m 0600 -o 1654 -g 1654 identity.enc /opt/pocketledger-security/identity/oci/identity.enc
```

## Configuration

Non-secret configuration:

```text
Encryption__KeyProvider=OciVault
Encryption__OciVault__CryptoEndpoint=https://<vault>-crypto.kms.<region>.oraclecloud.com
Encryption__OciVault__KeyId=<service-specific-key-ocid>
Encryption__OciVault__EncryptedCredentialPath=/run/pocketledger-oci/<service>.enc
Encryption__OciVault__UnlockSocketPath=/tmp/pocketledger-unlock/unlock.sock
Encryption__OciVault__TimeoutSeconds=10
Encryption__OciVault__MaxAttempts=3
```

The Compose override supplies the three distinct file names and Key OCIDs. The only persistent secret is each `*.enc` file. The external secret is each administrator-known passphrase, which is not stored on the VPS.

## Activate OCI KMS on an existing clean Local deployment

Perform these steps from the same repository and `.env` file used by the Local deployment. `COMPOSE_PROJECT_NAME` must remain unchanged throughout the procedure.

1. Confirm that the Local deployment is healthy and record the effective project name:

   ```bash
   docker compose config --quiet
   docker compose ls
   docker compose ps
   ```

2. Configure the OCI crypto endpoint and the three service-specific Key OCIDs in `.env`. Render both configurations before stopping anything:

   ```bash
   docker compose -f compose.yaml -f compose.oci-kms-migration.yaml config --quiet
   docker compose -f compose.yaml -f compose.oci-kms.yaml config --quiet
   ```

   The migration configuration must retain each service's Local `active.pfx` and key-ring mounts while adding its encrypted OCI credential mount. The long-term OCI configuration deliberately removes the Local certificate mounts.

3. Stop only the three application containers. Keep the databases stopped or running as appropriate for the backup procedure, but do not remove any container or volume:

   ```bash
   docker compose stop api web identity
   ```

4. Back up the complete security directory, including all three key rings, all three `active.pfx` files and the encrypted OCI credentials. Keep this backup separate from database backups. Do not continue until both backup sets are recoverable.

5. Rewrap the three key rings one at a time. Each command prompts for that service's encrypted-credential passphrase and performs an OCI encrypt/decrypt probe before touching the key-ring files:

   ```bash
   docker compose -f compose.yaml -f compose.oci-kms-migration.yaml run --rm --no-deps api rewrap-key-ring
   docker compose -f compose.yaml -f compose.oci-kms-migration.yaml run --rm --no-deps web rewrap-key-ring
   docker compose -f compose.yaml -f compose.oci-kms-migration.yaml run --rm --no-deps identity rewrap-key-ring
   ```

   Do not start the long-term OCI deployment until all three commands succeed. Every successful command reports its `.rewrap-backup-*` directory. Retain these backups and the old `active.pfx` files.

6. Verify without printing key payloads or ciphertext:

   ```bash
   security_directory=/opt/pocketledger-security
   find "$security_directory" -path '*/keys/key-*.xml' -print
   grep -l 'ociKmsWrappedKey' "$security_directory"/{api,web,identity}/keys/key-*.xml
   find "$security_directory" -name .rewrap-in-progress -print
   ```

   Every top-level key file must be listed by `grep`, and the final `find` must print nothing. A marker means an interrupted migration; follow the recovery instructions below instead of starting the applications.

7. Start the long-term OCI configuration. The application containers remain alive but not ready until manually unlocked:

   ```bash
   docker compose -f compose.yaml -f compose.oci-kms.yaml up -d --build
   docker compose -f compose.yaml -f compose.oci-kms.yaml ps
   ```

8. Unlock API, Web and Identity. The helper changes to the repository directory itself, so it consistently reads the deployment `.env` and its `COMPOSE_PROJECT_NAME`:

   ```bash
   ./tools/unlock-oci-kms.sh
   ```

9. Verify all three readiness endpoints, login, TOTP/recovery, finance reads and writes, background processing and an encrypted backup export. Restart each application once, confirm it returns to locked/unhealthy, unlock it again and repeat a protected read.

10. Keep the Local PFX files, pre-rewrap key-ring backup, encrypted credentials, database backups and administrator passphrases until an isolated database-and-key recovery test has succeeded. The long-term OCI Compose configuration does not mount the Local PFX files, but retaining them is required for rollback from the migration backups.

If a rewrap command fails, it reports a categorized exception chain without printing credential or key material. The original top-level key files are restored automatically for caught failures, the staging directory and marker are removed, and the `.rewrap-backup-*` directory is retained. Do not switch providers after a failed command. If the process was interrupted and a marker remains, restore the top-level key files from the backup directory recorded in the marker before retrying.

## Startup and manual unlock

Start or restart normally:

```bash
docker compose -f compose.yaml -f compose.oci-kms.yaml up -d --build
```

Each process stays alive but locked. `/health/live` returns 200; `/health/ready` returns 503 and normal HTTP routes return 503 until unlock. The OCI Compose override checks readiness, so containers show `unhealthy` while locked; Docker Compose does not restart a container merely because its health status is unhealthy, and `restart: unless-stopped` therefore does not create an unlock restart loop. After unlock the health check becomes healthy.

Unlock all three services with one operator command:

```bash
./tools/unlock-oci-kms.sh
```

The command prompts separately for the API, Web and Identity credential passphrases through each container's no-echo TTY. It sends each passphrase over a service-local Unix domain socket whose directory is mode `0700` and socket mode `0600`; no public HTTP unlock endpoint exists. Docker `exec` runs as the application UID, so filesystem permissions authorize the local client. Each service decrypts its own credential, constructs the OCI SDK authentication provider directly from memory, performs an OCI encrypt/decrypt probe against the configured Key OCID, validates its Data Protection key ring, and only then becomes ready. A wrong passphrase, corrupt file, KMS outage, wrong key or authentication failure leaves that service locked and reports a non-secret error.

After any process/container/VPS restart, repeat the unlock command. Unlocking one service does not unlock or weaken the other two.

The clean activation procedure requires all EF Core migrations to be complete before switching to OCI. The current hosts still check/apply database migrations before the manual unlock socket is available. A future release that introduces new schema migrations therefore needs an unlock-aware maintenance migration command or a separately reviewed rollout procedure; do not treat an ordinary OCI-mode restart as a migration runbook.

## Passphrase change and interruption recovery

On a host with the repository and .NET 10 SDK, run as the credential-file owner (UID 1654 in the supplied image):

```bash
sudo -u '#1654' dotnet run --project tools/PocketLedger.Security.Cli -- change-passphrase
```

The CLI decrypts in memory, generates a new salt and nonce, verifies the replacement, fsyncs a same-directory temporary file, sets mode `0600`, then atomically replaces the old file. Before the final rename an interruption leaves the original untouched; after it, the fully authenticated replacement is present. Restart and unlock the affected service to prove the new passphrase before discarding recovery knowledge of the old one. Because Compose mounts the containing directory, atomic replacement is visible to a recreated container.

## OCI API signing-key rotation

1. Generate a new passphrase-protected signing key on a trusted workstation.
2. Add its public key to the same dedicated OCI service user.
3. Create a new `.enc` using `create-oci-credential` and a temporary output name.
4. Keep the old `.enc` offline, atomically install the new file with mode `0600`, recreate the affected container and unlock it.
5. Verify `/health/ready`, login and protected data operations.
6. Only then delete/revoke the old OCI API key and retire the old encrypted credential.

PocketLedger never revokes OCI credentials automatically.

## Migration from the plaintext OCI design

For a deployment already using PL-96 OCI-wrapped key rings:

1. Stop API, Web and Identity.
2. On a trusted machine, convert each existing passphrase-protected PEM to its service-specific `.enc`.
3. Install the three `.enc` files and update to `compose.oci-kms.yaml`; remove plaintext `config` and `api-key.pem` only after retaining an offline recovery copy.
4. Start the services, run `./tools/unlock-oci-kms.sh`, verify readiness and protected reads/writes.
5. Confirm no plaintext OCI credential remains on VPS storage or in deployment backups, then securely retire the VPS copies.

For Local-to-OCI migration, use only the clean-deployment activation procedure above. Never mix a restored database with an unrelated key ring.

## Remaining PL-98 work

PL-99 does not implement runtime Linux/container hardening. Production follow-up must address core dumps, .NET diagnostics, swap, `ptrace`, `/proc` visibility, least-privilege identities, capability dropping, `no-new-privileges`, read-only root filesystems, and AppArmor/seccomp. The local socket path must be moved to an explicitly writable tmpfs when a read-only root filesystem is enabled.
