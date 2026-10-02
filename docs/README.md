# PocketLedger documentation

| Guide | Covers |
| --- | --- |
| [Deployment guide](deployment.md) | Full VPS rollout with Docker Compose: domains, secrets, encryption keys, internal TLS, subdomain migration, caching, backups. |
| [Database encryption](database-encryption.md) | Field-level encryption design, data classification, key rotation and recovery. |
| [OCI KMS](oci-kms.md) | Moving Data Protection key custody to an OCI Vault/KMS hardware security module: activation, unlock, rotation. |
| [Edge security](edge-security.md) | Caddy + CrowdSec configuration in front of the deployment, the Cloudflare trust boundary, verification and rollback. |

Start with the [deployment guide](deployment.md) for a first production rollout — it links out to the other three where the relevant step comes up. See the [project README](../README.md) for an overview, screenshots, and local development instructions.
