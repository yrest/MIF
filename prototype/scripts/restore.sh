#!/usr/bin/env bash
# Restores a backup made by backup.sh into this (new or existing) live node.
# Usage: scripts/restore.sh backups/mif-<stamp>.sql.gz
# Needs the .env that belongs to the backup. Replaces all current data in the node.
set -euo pipefail
cd "$(dirname "$0")/.."

dump=${1:?Usage: scripts/restore.sh backups/mif-<stamp>.sql.gz}
key=${dump%.sql.gz}.signing-key.pem
[ -f "$dump" ] && [ -f "$key" ] || { echo "Need both $dump and $key" >&2; exit 1; }

docker compose up -d postgres
until docker compose exec -T postgres pg_isready -U mif -d keycloak >/dev/null 2>&1; do sleep 1; done
docker compose stop keycloak mif-node demo-portal 2>/dev/null || true
gunzip -c "$dump" | docker compose exec -T postgres psql -q -v ON_ERROR_STOP=1 -U mif keycloak >/dev/null
docker compose run --rm --no-deps -T --entrypoint sh mif-node -c 'cat > /data/keys/signing-key.pem && chmod 600 /data/keys/signing-key.pem' < "$key"
docker compose up -d
echo "Restored. Check: curl -s localhost:8088/status"
