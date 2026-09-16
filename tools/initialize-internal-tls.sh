#!/usr/bin/env bash
set -euo pipefail
umask 077

if [[ $# != 1 || "$1" != /* ]]; then
    echo 'Usage: sudo bash tools/initialize-internal-tls.sh /absolute/security/directory' >&2
    exit 1
fi
security_directory=$1
tls_directory="$security_directory/internal-tls"
if [[ ! -d "$security_directory" ]]; then
    echo 'The security directory must already exist. Prepare the encryption keys first.' >&2
    exit 1
fi
if [[ -e "$tls_directory" ]]; then
    echo 'Refusing to overwrite existing internal TLS material.' >&2
    exit 1
fi

mkdir -m 700 -- "$tls_directory"
openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes -subj '/CN=PocketLedger internal CA/' \
    -addext 'basicConstraints=critical,CA:TRUE,pathlen:0' -addext 'keyUsage=critical,keyCertSign,cRLSign' \
    -keyout "$tls_directory/ca.key" -out "$tls_directory/ca.crt"
openssl req -new -newkey rsa:3072 -sha256 -nodes -subj '/CN=api/' \
    -keyout "$tls_directory/api.key" -out "$tls_directory/api.csr"
printf '%s\n' 'basicConstraints=critical,CA:FALSE' 'keyUsage=critical,digitalSignature,keyEncipherment' \
    'extendedKeyUsage=serverAuth' 'subjectAltName=DNS:api' > "$tls_directory/api.ext"
openssl x509 -req -sha256 -days 825 -in "$tls_directory/api.csr" -CA "$tls_directory/ca.crt" -CAkey "$tls_directory/ca.key" \
    -CAcreateserial -extfile "$tls_directory/api.ext" -out "$tls_directory/api.crt"
openssl pkcs12 -export -passout pass: -inkey "$tls_directory/api.key" -in "$tls_directory/api.crt" \
    -certfile "$tls_directory/ca.crt" -out "$tls_directory/api.pfx"
openssl verify -CAfile "$tls_directory/ca.crt" "$tls_directory/api.crt"
openssl x509 -in "$tls_directory/api.crt" -noout -checkhost api

rm -- "$tls_directory/api.key" "$tls_directory/api.csr" "$tls_directory/api.ext" "$tls_directory/api.crt" "$tls_directory/ca.srl"
chmod 600 -- "$tls_directory/ca.key"
chmod 644 -- "$tls_directory/ca.crt"
chmod 400 -- "$tls_directory/api.pfx"
if [[ $EUID == 0 ]]; then
    # The .NET runtime image uses APP_UID=1654. Caddy and Web only receive the public CA certificate.
    chown 1654:1654 -- "$tls_directory/api.pfx"
else
    echo 'Warning: run as root for production so api.pfx is assigned to the .NET container user.' >&2
fi
chmod 755 -- "$tls_directory"
echo 'Internal TLS material prepared. Keep ca.key private and back it up for API certificate renewal.'
