#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

# Do not allow Compose to initialize PostgreSQL on the underlying unencrypted filesystem.
if ! mountpoint -q /srv/pocketledger; then
    echo 'Unlock and mount /srv/pocketledger before running Compose.' >&2
    exit 1
fi
source_device=$(findmnt -n -o SOURCE --target /srv/pocketledger)
if [[ "$(readlink -f "$source_device")" != "$(readlink -f /dev/mapper/pocketledger-data)" ]]; then
    echo '/srv/pocketledger must be mounted from /dev/mapper/pocketledger-data.' >&2
    exit 1
fi
if ! cryptsetup status pocketledger-data | grep -Eq 'type:[[:space:]]+LUKS2'; then
    echo 'The pocketledger-data mapping must use LUKS2.' >&2
    exit 1
fi
# Keep keys off the unencrypted root disk after enabling storage encryption.
export POCKETLEDGER_SECURITY_DIRECTORY=/srv/pocketledger/security
repository_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repository_directory"
compose_files=(-f compose.yaml -f compose.encrypted-storage.yaml)
case "${POCKETLEDGER_OCI_KMS_MODE:-}" in
    enabled) compose_files+=(-f compose.oci-kms.yaml) ;;
    migration) compose_files+=(-f compose.oci-kms-migration.yaml) ;;
    "") ;;
    *) echo 'POCKETLEDGER_OCI_KMS_MODE must be empty, enabled or migration.' >&2; exit 1 ;;
esac
exec docker compose "${compose_files[@]}" "$@"
