# OCI Vault/KMS key provider (PL-96)

PocketLedger can protect its three independent ASP.NET Core Data Protection key rings with three OCI Vault/KMS keys. Application data remains encrypted locally by Data Protection. OCI receives only the small Data Protection master-key elements for wrap and unwrap operations; it never receives transaction, Identity or session payloads.

## Architecture and boundaries

`Encryption:KeyProvider` explicitly selects `Local` or `OciVault`. There is no fallback between providers. `Local` retains the certificate-backed behavior for self-hosted installations. `OciVault` uses the vault cryptographic endpoint with `AES_256_GCM` and stores the key OCID and key-version OCID beside each wrapped key-ring secret. Existing database ciphertext and purpose strings remain unchanged.

API, Web and Identity must use separate OCI keys and separate API credentials. A credential can call only `KEY_ENCRYPT` and `KEY_DECRYPT` for its own key. The OCI API signing private key is a revocable credential, not the KMS key. The non-exportable KMS key remains in OCI. A fully compromised running application can still use its credential while that credential remains valid; KMS primarily removes the wrapping key from VPS storage and adds centralized policy, revocation and audit controls.

## OCI resources and least-privilege policies

Create one vault, three symmetric AES keys and three dedicated non-interactive OCI users/groups:

- `PocketLedgerApiKmsClients` → API key;
- `PocketLedgerWebKmsClients` → Web key;
- `PocketLedgerIdentityKmsClients` → Identity key.

Grant exactly two permissions to each group. Substitute the compartment and key OCIDs:

```text
Allow group PocketLedgerApiKmsClients to use keys in compartment id <COMPARTMENT_OCID> where all {target.key.id = '<API_KEY_OCID>', request.permission = 'KEY_ENCRYPT'}
Allow group PocketLedgerApiKmsClients to use keys in compartment id <COMPARTMENT_OCID> where all {target.key.id = '<API_KEY_OCID>', request.permission = 'KEY_DECRYPT'}

Allow group PocketLedgerWebKmsClients to use keys in compartment id <COMPARTMENT_OCID> where all {target.key.id = '<WEB_KEY_OCID>', request.permission = 'KEY_ENCRYPT'}
Allow group PocketLedgerWebKmsClients to use keys in compartment id <COMPARTMENT_OCID> where all {target.key.id = '<WEB_KEY_OCID>', request.permission = 'KEY_DECRYPT'}

Allow group PocketLedgerIdentityKmsClients to use keys in compartment id <COMPARTMENT_OCID> where all {target.key.id = '<IDENTITY_KEY_OCID>', request.permission = 'KEY_ENCRYPT'}
Allow group PocketLedgerIdentityKmsClients to use keys in compartment id <COMPARTMENT_OCID> where all {target.key.id = '<IDENTITY_KEY_OCID>', request.permission = 'KEY_DECRYPT'}
```

Do not grant key creation, deletion, rotation, export, vault administration or access to the other two application keys.

## Credential directories

Create one private OCI directory per host under `POCKETLEDGER_SECURITY_DIRECTORY`:

```text
api/oci/config
api/oci/api-key.pem
web/oci/config
web/oci/api-key.pem
identity/oci/config
identity/oci/api-key.pem
```

Each directory is mounted only into its corresponding container. Its `config` uses the container path, not the host path:

```ini
[DEFAULT]
user=<HOST_SPECIFIC_USER_OCID>
fingerprint=<API_KEY_FINGERPRINT>
tenancy=<TENANCY_OCID>
region=eu-frankfurt-1
key_file=/run/pocketledger-oci/api-key.pem
```

Directories must be mode `0700`; `config` and private keys must be `0600` and owned by container UID 1654. Do not commit these files or include them in ordinary deployment archives.

Set the common crypto endpoint and the three key OCIDs in `.env`. Copy the values from `.env.example`. The crypto endpoint is the vault's HTTPS **Cryptographic Endpoint**, not the OCI Console URL or management endpoint.

## Local-to-OCI migration

The migration rewraps only the Data Protection key-ring secrets. Database rows, BFF sessions and Identity data are not rewritten. Schedule downtime and retain an offline copy of the complete security directory before starting.

1. Build the new images and stop all three application containers. Keep databases stopped from application writes during the operation.
2. Provision the OCI resources, policies and three credential directories.
3. Run each host's migration with the temporary override. It mounts both the old certificate and the new OCI credential:

   ```bash
   docker compose -f compose.yaml -f compose.oci-kms-migration.yaml run --rm --no-deps api rewrap-key-ring
   docker compose -f compose.yaml -f compose.oci-kms-migration.yaml run --rm --no-deps web rewrap-key-ring
   docker compose -f compose.yaml -f compose.oci-kms-migration.yaml run --rm --no-deps identity rewrap-key-ring
   ```

   Every command decrypts all source keys, wraps and verifies every target key in a staging directory, then replaces individual key files atomically. Originals remain in a private `.rewrap-backup-<UTC timestamp>` directory inside the corresponding key directory. A failed commit restores every file already replaced. An abrupt process or host interruption leaves a `.rewrap-in-progress` marker; normal application startup then fails closed until the operator restores the backup path recorded in that marker.
4. Start with the long-term override, which removes the old encryption-certificate mounts:

   ```bash
   docker compose -f compose.yaml -f compose.oci-kms.yaml config --quiet
   docker compose -f compose.yaml -f compose.oci-kms.yaml up -d --force-recreate api web identity
   ```

5. Verify login/TOTP, existing sessions according to the planned session policy, financial reads and writes, background processing, encrypted backup export and restart of each host. Inspect key XML only for structure: it must contain `ociKmsWrappedKey` and must not contain plaintext `masterKey` values.
6. Move the old certificate-protected backup directories and certificates to offline recovery storage. Do not destroy them until a separately restored OCI-protected deployment has successfully read the data.

For LUKS deployments, use `POCKETLEDGER_OCI_KMS_MODE=migration` with `tools/compose-encrypted-storage.sh` for step 3, and `POCKETLEDGER_OCI_KMS_MODE=enabled` for subsequent Compose operations. The wrapper retains all existing mount and LUKS checks.

## Failure behavior and recovery

Missing configuration, invalid OCIDs, a non-HTTPS endpoint, inaccessible credentials, denied IAM requests, timeout, damaged ciphertext or failed KMS unwrap abort startup. PocketLedger never falls back to `Local` and never generates a replacement key for existing encrypted data.

The SDK retries only bounded transient failures. `TimeoutSeconds` is restricted to 1–120 and `MaxAttempts` to 1–5. Do not set long values that make container failure detection ineffective.

To roll back before deleting the local certificates, stop the applications, restore every top-level `key-*.xml` from its `.rewrap-backup-*` directory, and start with base `compose.yaml` using `KeyProvider=Local`. Never mix a restored database with a newly generated key ring.

## Rotation

- **OCI key version rotation:** rotate the existing logical OCI key. New wraps use the new active version; the stored key-version OCID lets OCI decrypt historical key-ring entries. Keep old key versions enabled while any key ring or backup references them.
- **Move to another OCI key:** temporarily allow the host credential to decrypt the old key and encrypt with the new key, change the host's Key OCID, stop the host and rerun `rewrap-key-ring`. Remove old-key permission only after restart and recovery validation.
- **API credential rotation:** upload the new public API key, atomically replace the mounted config/private key, recreate the affected host, validate it, then revoke the old API key. This does not rewrap key-ring data.
- **Compromise:** revoke the affected API credential immediately. Rotate to a new logical OCI key and rewrap from a trusted environment if the old credential might have been used. Ordinary version rotation alone does not remediate a credential that can still call decrypt.

Back up the OCI-protected key rings after every migration. Database backups still require their matching key rings, OCI keys/key versions and a credential authorized to decrypt them.
