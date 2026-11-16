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

All ports are bound to loopback. This is a simulation for trying the idea and for the automated drill; for real use see the live configuration below.

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
├─ docker-compose.yml         simulation: HTTP on localhost, stand-in for Entra
├─ docker-compose.mesh.yml    simulation over HTTPS, for a second device
├─ docker-compose.live.yml    live configuration: real Entra, production mode, kept data
├─ scripts/                   init-live, add-app-client, backup, restore
├─ caddy/                     TLS proxy for mesh mode, and an example for your own front proxy
├─ .env.example               selects mesh mode (init-live.sh writes .env for the live configuration)
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

## Live configuration: one node for real use

`docker-compose.yml` is a simulation. `docker-compose.live.yml` is the same software set up to be kept: a real Entra tenant as the upstream, the broker in production mode, secrets generated on the machine, and data that is never wiped. Start small with a pilot group and one application, then add people and applications; nothing is rebuilt along the way.

**Decide these first, because they cannot change later without everyone enrolling again:**

- **The hostname**, for example `login.example.org`. Passkeys and every application are bound to it.
- **That the data is kept.** Never run `docker compose down -v` against the live configuration.

The machine can change. `scripts/backup.sh` and `scripts/restore.sh` move the node to another host under the same hostname, and existing passkeys keep working.

### What you need

- A TLS-terminating proxy that you run, reachable under the hostname, which forwards to this machine over your VPN or mesh. [`caddy/live-front.Caddyfile.example`](caddy/live-front.Caddyfile.example) is a Caddy site block that admits only VPN and mesh clients and exposes only the sign-in paths.
- This machine's address on the mesh, where that proxy connects.
- An Entra app registration for the broker:
  - redirect URI (type Web) `https://<hostname>/realms/mif/broker/entra/endpoint`;
  - a client secret;
  - optional ID token claims `email`, `given_name` and `family_name`, otherwise the broker asks each user to type them at first sign-in;
  - app roles for any roles you want carried into tokens.
- Optionally a second app registration with a secret and no permissions, for the synthetic sign-in probe. Without it the node checks Entra's discovery document and signing keys only.

### Set it up

```bash
./scripts/init-live.sh login.example.org 100.64.0.10   # hostname, this machine's mesh address
# fill in the ENTRA_* values in .env
docker compose up -d --build
curl -s localhost:8088/status                           # expect Normal and upstreamHealthy true
```

Then:

- **Administration console:** `http://localhost:8085/admin/` on this machine only, user `admin`, password `MIF_ADMIN_PASSWORD` from `.env`. Create a personal administrator and delete the temporary one.
- **Diagnostic portal:** `http://localhost:5080` on this machine only. Sign in to see whether a session is LIVE or CONTINUITY and which claims it carries.
- **Connect an application:** `./scripts/add-app-client.sh <client-id> '<redirect-uri>'` registers it and prints what to configure in the application. The secret is written to a file, not shown.
- **Rehearse an outage:** Entra cannot be stopped, so force degraded mode with the operator command above and release it with `Auto`.
- **Back up:** `./scripts/backup.sh` writes the database and the signing key to `backups/`. Keep them, and `.env`, somewhere safe: together they are the node.

### Things to know

- Anything that can reach the broker's port on the mesh address can pose as your proxy. Limit that port to the proxy with a mesh access policy.
- The proxy sees VPN and mesh client addresses only if clients resolve the hostname to the proxy's VPN or mesh address. Point it there in the DNS those clients use.
- An application that does not understand the `mif_idp` claim gives full access in degraded mode. Connect only applications where that is acceptable, or that enforce the restriction themselves.
- Once an application signs in through the node, it depends on the node every day, not only during an outage. The host must stay awake and start the stack after a reboot.
- By default any account in the tenant can sign in. To limit it, require assignment on the Entra app registration.

**Verified:** this configuration in production mode behind a proxy built from the example, with the stand-in as the upstream: the full outage drill, application registration, and a backup, wipe and restore round trip. **Not verified:** a real Entra tenant.

## Mesh mode: the simulation over HTTPS

For trying the simulation from a second device without a tenant. It puts a TLS proxy with its own test certificate authority in front and publishes each service on its own port of one hostname. Select it in `.env` (see [`.env.example`](.env.example)), then recreate the stack with `docker compose down -v && docker compose up -d --build`.

| URL | Service |
|---|---|
| `https://MIF_HOST:5443` | Demo portal |
| `https://MIF_HOST:8443` | MIF broker |
| `https://MIF_HOST:8444` | Stand-in for Entra |
| `https://MIF_HOST:8448` | MIF node, public endpoints |

Every device you test from must trust the test authority's root certificate:

```bash
docker compose exec -T caddy cat /data/caddy/pki/authorities/local/root.crt > mif-test-root-ca.crt
```

Remove that trust when you are done: whoever holds the matching private key (it lives in the `caddy-data` volume) can impersonate any site to a device that trusts it. `docker compose down -v` destroys the key.

## Limits

Of the simulation (`docker-compose.yml`, with or without mesh mode):

- Keycloak runs in development mode, and every secret is a throwaway value that is public in this repository.

Of the live configuration:

- One node. There is no standby yet; see the profile for the intended layout.
- The broker and the node share one database user.
- No alerting. Decide how you learn that the node has gone degraded, on a channel that does not depend on what is failing.
