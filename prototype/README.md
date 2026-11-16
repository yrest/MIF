# MIF prototype: private-mesh profile

A local simulation of one MIF node, built to prove the [private-mesh profile](../docs/profiles/private-mesh.md): sign in through an upstream identity provider, lose that provider, keep signing in with a passkey under restrictions, and return to normal when it comes back.

Nothing here talks to Microsoft. A second Keycloak plays the part of Entra so that the outage can be switched on and off.

## What runs

| Service | Port | Role |
|---|---|---|
| `keycloak` | 8080 | The MIF broker: federates the upstream, holds passkeys, issues tokens |
| `fake-entra` | 8090 | Stand-in for Entra. Stop it to simulate the outage |
| `mif-node` | 8088, 8081 (loopback) | Snapshot service and continuity controller (ASP.NET Core) |
| `demo-portal` | 5080 | Example application that trusts only the MIF node |
| `postgres` | internal | State for the broker and the node |

All ports are bound to loopback. To reach the stack from another device, use mesh mode below.

## Run it

Requires Docker with Compose.

```bash
cd prototype
docker compose up -d --build
```

First start takes about a minute. The node may report `Degraded` for a few seconds while the upstream container boots; wait until <http://localhost:8088/status> shows `"mode":"Normal"`.

### Walk through an outage by hand

Use a browser with a passkey provider (Windows Hello, Touch ID, a phone, or a security key).

1. Open <http://localhost:5080> and sign in as `alice` / `alice-dev-only`. The portal shows a **LIVE** session.
2. Choose **Enrol a passkey for outages** and register one.
3. Stop the upstream: `docker compose stop fake-entra`. Within about ten seconds `/status` reports `Degraded`.
4. Sign out, then sign in again. The sign-in page now offers only the passkey. The portal shows a **CONTINUITY** session.
5. **View records** works. **Approve payment** is refused with 403, although Alice still holds the role.
6. Bring the upstream back: `docker compose start fake-entra`. The node returns to `Normal` and ends all sessions at the broker.
7. The portal still shows **CONTINUITY** until you sign out and in again, because its own session lasts up to an hour. The next sign-in goes through the upstream and shows **LIVE**.

`bob` / `bob-dev-only` is a second user without the approver role.

### Operator commands

The operator port is bound to loopback. In a deployment you would reach it over SSH.

```bash
# Who holds a snapshot, and until when
curl -s localhost:8081/operator/snapshots

# Revoke a user for the rest of the outage
curl -s -X POST localhost:8081/operator/revoke -H 'content-type: application/json' \
  -d '{"username":"alice","reason":"laptop reported stolen"}'

# Force degraded mode (a drill, or an outage the probes cannot see), then release it
curl -s -X POST localhost:8081/operator/mode -H 'content-type: application/json' \
  -d '{"override":"ForceDegraded","reason":"quarterly drill"}'
curl -s -X POST localhost:8081/operator/mode -H 'content-type: application/json' \
  -d '{"override":"Auto","reason":"drill over"}'

# Audit trail, with a check that its hash chain is intact
curl -s 'localhost:8081/operator/audit?max=50'
```

From PowerShell, use `Invoke-RestMethod`:

```powershell
Invoke-RestMethod http://localhost:8081/operator/snapshots
Invoke-RestMethod -Method Post http://localhost:8081/operator/revoke -ContentType 'application/json' `
  -Body (@{ username = 'alice'; reason = 'laptop reported stolen' } | ConvertTo-Json)
```

## Tests

Unit tests cover snapshot signing, the mode state machine, eligibility rules, revocation, recovery and the audit chain. They need the .NET 10 SDK and no running services.

```bash
dotnet test Mif.slnx
```

The end-to-end drill drives a real browser with a virtual passkey through the full story against the running stack, stopping and starting `fake-entra` itself. Run it on the machine that hosts the stack.

```bash
cd e2e
npm install
npx playwright install chromium
npm run drill
```

In mesh mode, pass the hostname and let Node trust the test certificate authority (the browser uses the system trust store):

```bash
MIF_HOST=<same as in .env> NODE_EXTRA_CA_CERTS=../mif-test-root-ca.crt npm run drill
```

## Layout

```
prototype/
├─ docker-compose.yml         default: HTTP on localhost, stand-in for Entra
├─ docker-compose.mesh.yml    adds HTTPS on one hostname for other devices
├─ docker-compose.entra.yml   real Entra tenant instead of the stand-in
├─ .env.example               selects the mode
├─ caddy/Caddyfile            TLS proxy for mesh mode
├─ Dockerfile                 builds either .NET service
├─ keycloak/
│  ├─ mif-realm.json          broker realm: upstream IdP, both sign-in flows, clients
│  └─ upstream-realm.json     the stand-in for Entra, with test users
├─ src/
│  ├─ Mif.Node/               snapshot service, continuity controller, audit log
│  ├─ Mif.Client/             claim helpers and the "live identity" policy for ASP.NET Core apps
│  └─ Mif.DemoApp/            example application
├─ tests/Mif.Node.Tests/
└─ e2e/outage-drill.cjs
```

## Other modes

The default is plain HTTP on localhost, which only works from the machine running the stack. Two more modes are selected in a `.env` file; copy [`.env.example`](.env.example) and edit it.

Switching modes changes URLs that are written into the realms at first start, so always recreate the stack afterwards:

```bash
docker compose down -v && docker compose up -d --build
```

This deletes all users and passkeys. Passkeys are bound to the hostname and would not carry over anyway.

### Mesh mode: reach it from other devices

Passkeys need HTTPS on a real hostname, so mesh mode puts a TLS proxy (Caddy) in front and publishes each service on its own port of one hostname.

```
COMPOSE_FILE=docker-compose.yml:docker-compose.mesh.yml
MIF_HOST=<a name that resolves to this machine on the VPN or mesh>
```

| URL | Service |
|---|---|
| `https://MIF_HOST:5443` | Demo portal |
| `https://MIF_HOST:8443` | MIF broker |
| `https://MIF_HOST:8444` | Stand-in for Entra |
| `https://MIF_HOST:8448` | MIF node, public endpoints |

Certificates come from a private certificate authority that Caddy creates on first start. Every device you test from must trust its root certificate:

```bash
docker compose exec -T caddy cat /data/caddy/pki/authorities/local/root.crt > mif-test-root-ca.crt
```

Install that file as a trusted root on each test device, and remove it when you are done: whoever holds the matching private key (it lives in the `caddy-data` volume) can impersonate any site to a device that trusts it. `docker compose down -v` destroys the key.

Use one hostname for every network path. A passkey enrolled under one name does not work under another.

### A real Entra tenant

**Not yet verified against a real tenant.** Add `docker-compose.entra.yml` to `COMPOSE_FILE` and fill in the `ENTRA_*` values in `.env`. The stand-in is then not started.

In Entra you need an app registration for the broker:

- Redirect URI (type Web): `https://MIF_HOST:8443/realms/mif/broker/entra/endpoint` in mesh mode, or `http://localhost:8080/realms/mif/broker/entra/endpoint` on localhost.
- A client secret.
- Optional ID token claims `email`, `given_name` and `family_name`, otherwise the broker asks each user to type them at first sign-in.
- An app role with the value `finance-approver`, assigned to the test user who should be allowed the high-risk action.

A second app registration with a secret and no permissions enables the synthetic sign-in probe. Without it the node checks Entra's discovery document and signing keys only.

Entra cannot be stopped to simulate an outage, so force degraded mode with the operator command above.

## Not for deployment as is

- Keycloak runs in development mode over plain HTTP.
- Every secret in `docker-compose.yml` and the realm files is a throwaway value.
- The broker and the node share one database user.
- There is no standby node; see the profile for the intended Azure and on-prem layout.
