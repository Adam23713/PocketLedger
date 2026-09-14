#!/usr/bin/env bash
set -euo pipefail
umask 077

if [[ $# != 1 || "$1" != /* ]]; then
    echo 'Usage: bash tools/initialize-encryption-keys.sh /absolute/security/directory' >&2
    exit 1
fi
security_directory=$1
if [[ -e "$security_directory" ]]; then
    echo 'Refusing to overwrite an existing security directory. Keep its keys for recovery.' >&2
    exit 1
fi
mkdir -m 700 -- "$security_directory"
for application in api web identity; do
    application_directory="$security_directory/$application"
    mkdir -m 700 -- "$application_directory" "$application_directory/keys" "$application_directory/certificates"
    openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes -subj "/CN=PocketLedger $application data protection/" \
        -keyout "$application_directory/certificates/private.pem" -out "$application_directory/certificates/certificate.pem"
    openssl pkcs12 -export -passout pass: -inkey "$application_directory/certificates/private.pem" \
        -in "$application_directory/certificates/certificate.pem" -out "$application_directory/certificates/active.pfx"
    # The same private key remains in active.pfx; do not leave redundant PEM copies.
    rm -- "$application_directory/certificates/private.pem" "$application_directory/certificates/certificate.pem"
done
if [[ $EUID == 0 ]]; then
    # The .NET runtime image uses APP_UID=1654. PostgreSQL never mounts this directory.
    chown -R 1654:1654 -- "$security_directory"
fi
echo 'Key directories prepared. Back up each key ring and its private certificates separately from database backups.'
