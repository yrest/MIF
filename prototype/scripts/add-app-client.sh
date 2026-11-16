#!/usr/bin/env bash
# Registers an application with the MIF broker so it can sign people in through it.
# Usage: scripts/add-app-client.sh <client-id> <redirect-uri> [<redirect-uri> ...]
#   e.g. scripts/add-app-client.sh chat 'https://chat.example.org/*'
# The client secret is written to app-client-<client-id>.secret (ignored by git), not printed.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ $# -lt 2 ]; then
  sed -n '2,5p' "$0" | sed 's/^# \{0,1\}//'
  exit 2
fi
client_id=$1; shift
set -a; . ./.env; set +a
kcadm() { docker compose exec -T keycloak /opt/keycloak/bin/kcadm.sh "$@"; }

redirects=$(printf '"%s",' "$@"); redirects="[${redirects%,}]"
claim() { # name, mapper type, extra config
  printf '{"name":"%s","protocol":"openid-connect","protocolMapper":"%s","config":{%s,"claim.name":"%s","jsonType.label":"String","id.token.claim":"true","access.token.claim":"true","userinfo.token.claim":"true"}}' "$1" "$2" "$3" "$1"
}
mappers="[$(claim mif_idp oidc-usersessionmodel-note-mapper '"user.session.note":"identity_provider"'),$(claim roles oidc-usermodel-realm-role-mapper '"multivalued":"true"')]"

kcadm config credentials --server http://localhost:8080 --realm master --user admin --password "$MIF_ADMIN_PASSWORD" >/dev/null
id=$(printf '{"clientId":"%s","publicClient":false,"standardFlowEnabled":true,"directAccessGrantsEnabled":false,"redirectUris":%s,"attributes":{"post.logout.redirect.uris":"+"},"protocolMappers":%s}' \
  "$client_id" "$redirects" "$mappers" | kcadm create clients -r mif -f - -i)

umask 077
kcadm get "clients/$id/client-secret" -r mif --fields value --format csv --noquotes > "app-client-$client_id.secret"
cat <<INFO
Registered '$client_id'. Configure the application with:
  Issuer / discovery URL  https://$MIF_HOST/realms/mif
  Client ID               $client_id
  Client secret           in app-client-$client_id.secret
  Scopes                  openid profile email
Tokens carry 'preferred_username', 'email', 'roles', and 'mif_idp' (absent in a degraded-mode session).
INFO
