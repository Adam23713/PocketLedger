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

## Initial provisioning

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

For Local-to-OCI migration, keep the old certificates temporarily and run the interactive migration commands; each asks for its encrypted OCI credential passphrase:

```bash
docker compose -f compose.yaml -f compose.oci-kms-migration.yaml run --rm --no-deps api rewrap-key-ring
docker compose -f compose.yaml -f compose.oci-kms-migration.yaml run --rm --no-deps web rewrap-key-ring
docker compose -f compose.yaml -f compose.oci-kms-migration.yaml run --rm --no-deps identity rewrap-key-ring
```

The existing staging, backup and `.rewrap-in-progress` recovery behavior is unchanged. Restore the backup path recorded in the marker before retrying an interrupted migration. Never mix a restored database with an unrelated key ring.

## Remaining PL-98 work

PL-99 does not implement runtime Linux/container hardening. Production follow-up must address core dumps, .NET diagnostics, swap, `ptrace`, `/proc` visibility, least-privilege identities, capability dropping, `no-new-privileges`, read-only root filesystems, and AppArmor/seccomp. The local socket path must be moved to an explicitly writable tmpfs when a read-only root filesystem is enabled.
