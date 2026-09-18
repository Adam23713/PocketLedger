#!/usr/bin/env bash
set -euo pipefail

compose_files=(-f compose.yaml -f compose.oci-kms.yaml)
for service in api web identity; do
  printf 'Unlocking %s (use its dedicated credential passphrase)\n' "$service"
  docker compose "${compose_files[@]}" exec "$service" dotnet /app/security-cli/PocketLedger.Security.Cli.dll unlock
done

printf 'API, Web and Identity unlock commands completed successfully.\n'
