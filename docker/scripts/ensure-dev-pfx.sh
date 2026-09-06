#!/bin/sh
# Generates an ephemeral development PKCS#12 for OpenSignature compose stacks.
# Never bake production certificates into images. Mount /certs as a named volume.
set -eu

CERTS_DIR="${CERTS_DIR:-/certs}"
PFX_PATH="${PFX_PATH:-$CERTS_DIR/dev.pfx}"
PFX_PASSWORD="${PFX_PASSWORD:-opensignature-dev}"
SUBJECT="${PFX_SUBJECT:-/CN=OpenSignature Dev}"

mkdir -p "$CERTS_DIR"

if [ -f "$PFX_PATH" ]; then
  echo "Development PFX already present at $PFX_PATH"
  exit 0
fi

echo "Generating development PFX at $PFX_PATH"
apk add --no-cache openssl >/dev/null

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

openssl req -x509 -newkey rsa:2048 \
  -keyout "$TMP_DIR/key.pem" \
  -out "$TMP_DIR/cert.pem" \
  -days 730 \
  -nodes \
  -subj "$SUBJECT"

openssl pkcs12 -export \
  -out "$PFX_PATH" \
  -inkey "$TMP_DIR/key.pem" \
  -in "$TMP_DIR/cert.pem" \
  -passout "pass:$PFX_PASSWORD"

chmod 644 "$PFX_PATH"
echo "Wrote development PFX ($PFX_PATH)"
