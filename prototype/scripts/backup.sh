#!/usr/bin/env bash
# Writes a backup of the live node to prototype/backups/: the database (users, passkeys, snapshots,
# audit log, broker keys) and the snapshot signing key. Together with .env this is everything
# needed to restore the node, or to move it to another machine under the same hostname.
# The output is as sensitive as the node itself.
set -euo pipefail
cd "$(dirname "$0")/.."

stamp=$(date -u +%Y%m%dT%H%M%SZ)
umask 077
mkdir -p backups
docker compose exec -T postgres pg_dump -U mif --clean --if-exists keycloak | gzip > "backups/mif-$stamp.sql.gz"
docker compose exec -T mif-node cat /data/keys/signing-key.pem > "backups/mif-$stamp.signing-key.pem"
gzip -t "backups/mif-$stamp.sql.gz"
echo "Wrote backups/mif-$stamp.sql.gz and backups/mif-$stamp.signing-key.pem"
