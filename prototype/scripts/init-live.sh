#!/usr/bin/env bash
# Creates prototype/.env for the live configuration, with freshly generated secrets.
# Usage: scripts/init-live.sh <hostname> <bind-address>
#   hostname      the permanent public name of the node, e.g. login.example.org
#   bind-address  this machine's address on the mesh, where your front proxy connects
set -euo pipefail
cd "$(dirname "$0")/.."

if [ $# -ne 2 ]; then
  sed -n '2,5p' "$0" | sed 's/^# \{0,1\}//'
  exit 2
fi
if [ -e .env ]; then
  echo "Refusing to overwrite the existing .env. Its secrets match data that already exists." >&2
  exit 1
fi

secret() { openssl rand -hex 24; }
umask 077
cat > .env <<ENV
# Live configuration for the MIF node. Keep this file private: it is ignored by git.
COMPOSE_FILE=docker-compose.live.yml
MIF_HOST=$1
MIF_BIND_ADDRESS=$2

# Generated. These match the data in the Docker volumes, so do not regenerate them.
MIF_DB_PASSWORD=$(secret)
MIF_ADMIN_PASSWORD=$(secret)
MIF_NODE_CLIENT_SECRET=$(secret)
MIF_PORTAL_CLIENT_SECRET=$(secret)

# Fill these in from the Entra app registration for the broker.
ENTRA_TENANT_ID=
ENTRA_BROKER_CLIENT_ID=
ENTRA_BROKER_CLIENT_SECRET=

# Optional: a second app registration with a secret and no permissions, for the sign-in probe.
ENTRA_PROBE_CLIENT_ID=
ENTRA_PROBE_CLIENT_SECRET=
ENV
echo "Created .env. Fill in the ENTRA_* values, then run: docker compose up -d --build"
