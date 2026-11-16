# MIF deployment profile: private mesh (v0)

**Status:** draft, backed by a working prototype in [`prototype/`](../../prototype/README.md).
**Scope:** one organisation running one MIF node on its own private network, with Microsoft Entra ID as the single upstream identity provider.

This profile is the first concrete deployment of the [MIF specification](../../spec.md). It answers one question: *when Entra cannot sign people in, how do they still reach the services we host ourselves?*

## 1. What this gives you, and what it cannot

During an Entra sign-in outage, people who signed in through Entra recently can sign in to **services that trust the MIF node** using a passkey. Their access is limited by policy, time-boxed, logged, and ends when Entra returns.

It does **not** restore Microsoft 365. Outlook, Teams and SharePoint accept only Entra tokens, so no token from a MIF node will open them. That is why the plan has two phases:

| Phase | Delivers | Helps when |
|---|---|---|
| 1. Identity (this profile) | A sign-in path that survives an Entra outage | Entra sign-in is down |
| 2. Communication | A self-hosted chat service and contact directory that trust the MIF node | Outlook or Teams is down, with or without Entra |

Phase 1 alone would not have helped in an outage where Outlook was down and sign-in still worked. It is the foundation phase 2 stands on: the backup channel needs an identity that does not fail together with Microsoft.

## 2. Topology

```
                         ┌──────────────────────── Azure VM ────────────────────────┐
  laptop / phone         │                                                          │
  ┌────────────┐  WireGuard (static keys, UDP)   ┌─────────┐   ┌──────────┐         │
  │ WG client  ├─────────┼────────────────────► │ reverse │──►│ Keycloak │ broker  │──► Entra
  │ NetBird    │         │                       │  proxy  │   ├──────────┤         │   (normal mode only)
  │ passkey    │         │                       │         │──►│ mif-node │         │
  └─────┬──────┘         │                       └─────────┘   ├──────────┤         │
        │                │                                     │ Postgres │─────────┼──┐
        │                └─────────────────────────────────────┴──────────┴─────────┘  │ streaming
        │  NetBird overlay (second path, peer to peer)                                  │ replication
        │                ┌──────────────── on-prem container host ──────────────────┐   │
        └───────────────►│  Keycloak (stopped) · mif-node (stopped) · Postgres replica ◄─┘
                         └───────────────────────────────────────────────────────────┘
```

- **Path A, WireGuard.** The hub runs on the Azure VM. Peers authenticate with static keys, so reaching the node never needs a login.
- **Path B, NetBird.** An independent overlay. It reaches the Azure node if the WireGuard hub fails, and it reaches the on-prem standby if Azure is unreachable.
- **One issuer name.** Clients and applications use a single hostname for the node, for example `id.mesh.example`. It resolves to the active node on whichever path is in use. Token issuer checks depend on this name never changing.

## 3. Dependency rules

The node is only useful if it stays up when Microsoft identity is down. Hosting it in Azure is workable, because a running VM does not need Entra to keep running, but every item below removes a hidden dependency that would otherwise fail in the same outage.

| Rule | Why |
|---|---|
| No Key Vault, managed identity or Entra-authenticated database at runtime | All of them need an Entra token. The snapshot signing key therefore lives on local disk (see §6.4). |
| Container images are already on the host | A pull from a registry that authenticates through Entra fails during the outage. |
| Administration by SSH key over WireGuard | The Azure portal, Bastion and Entra-based SSH login all need Entra. |
| WireGuard uses static keys only | A mesh login that redirects to Entra is a circular dependency. |
| NetBird sign-in must not go through Entra | Use setup keys for devices, or point NetBird at the MIF broker as its identity provider. Turn off login expiry for devices that must work in an outage. Check this against the NetBird version you deploy. |
| The issuer hostname resolves without public or Azure DNS | Use mesh DNS or a hosts entry pushed with the tunnel configuration. |
| The TLS certificate does not need renewing during an outage | Use an internal CA, or a public certificate with a renewal alarm well before expiry. |
| Only the WireGuard UDP port is exposed publicly | The broker and node listen on the mesh interface. The node's operator port listens on loopback. |

Sharing the VM with the VPN proxy is fine for a pilot. Be aware that the tunnel endpoint and the identity signing keys then share one blast radius: a compromise of that VM is a compromise of both.

## 4. How the spec maps to this profile

| Spec component (§7.2) | In this profile |
|---|---|
| Federation Gateway | Keycloak realm `mif`, brokering Entra over OIDC |
| Identity Normalisation | Keycloak identity-provider mappers (Entra app roles to realm roles, re-synced on every live sign-in) |
| Subject Linking | Keycloak's federated identity link, one upstream per user |
| Policy Engine | Node configuration (snapshot lifetime, thresholds) plus an application-side authorisation policy |
| Snapshot Service | `mif-node` (ASP.NET Core) |
| Token Service | Keycloak. Applications trust the realm's issuer |
| Continuity Controller | `mif-node` |
| Audit | `mif-node`, hash-chained log in Postgres |
| Trust Anchor Management | Keycloak realm keys for tokens, a node-held ES256 key for snapshots |

Not implemented in v0: the *partitioned federation* and *emergency continuity* modes, more than one upstream, and assurance levels above "verified live" versus "continuity". §11 lists where the prototype falls short of requirements the spec calls mandatory.

## 5. Modes and what people see

### Normal mode

1. The application redirects to the node. The node redirects straight to Entra.
2. Entra authenticates the user. The node creates or updates the local account and re-syncs roles.
3. Within seconds the node issues a signed **identity snapshot** for that sign-in and marks the user eligible for continuity.
4. A user without a passkey is asked to register one at their next sign-in. Passkeys can only be registered while signed in live.

### Degraded mode

1. The node's probes fail for long enough (§7) and it switches the broker to the continuity sign-in flow.
2. The sign-in page offers one option: sign in with a passkey.
3. The broker admits the user only if they are currently eligible, meaning they hold a valid snapshot.
4. The application receives a token that is marked as a continuity session and applies its restrictions.

A user who is still signed in from before the outage keeps that session until it idles out or reaches its maximum age. It was verified live when it started.

### Recovery

When the probes succeed for long enough, the node switches the broker back and **ends every session at the broker**. Everyone signs in again through Entra, so no session opened with a passkey regains full access without a live verification.

An application keeps its own session until that expires. A user who signed in with a passkey therefore stays in the restricted session, in that application, until they sign out and in again or the application's session ends. Keep application sessions short for this reason.

## 6. Identity snapshots

Schema: [`schemas/identity-snapshot.schema.json`](../../schemas/identity-snapshot.schema.json). Example: [`examples/sample-snapshot.json`](../../examples/sample-snapshot.json).

### 6.1 Rules

- A snapshot is created **only** by a sign-in that Entra verified live.
- It expires a fixed time after that verification. The default is 72 hours.
- A passkey sign-in never creates or renews one. Continuity access therefore cannot extend itself, and 72 hours is also the longest outage a user can ride out.
- It carries the subject, the upstream that verified it, the verification time, and business roles. Nothing else.

Choosing the lifetime is a trade-off. Longer covers people who were on leave before the outage began. Shorter limits how long a since-disabled account stays usable. Anyone whose last live sign-in is older than the lifetime has no continuity access at all.

### 6.2 How eligibility is enforced

The node keeps one broker role, `continuity-eligible`, equal to "holds a snapshot that verifies, has not expired and is not revoked". It reconciles this every few seconds in both directions: a role granted by hand in the broker is removed and logged.

This is a deliberate v0 simplification. The broker checks the role at sign-in; it does not verify the snapshot signature itself. The node verifies signatures in the reconcile loop. Verifying inside the sign-in flow would need a custom Keycloak authenticator and is listed under next steps.

### 6.3 Revocation

During an outage the node cannot learn that Entra disabled an account. Two controls cover that gap:

- **Lifetime.** Access ends at most one snapshot lifetime after the last live sign-in.
- **Local revocation.** An operator revokes a user on the node. Their snapshots are revoked, their sessions end, and passkey sign-in is refused. The revocation holds until Entra next verifies the user live, at which point Entra is the source of truth again.

Local revocation is the manual form of the spec's kill switch (§15.4): it is independent of Entra, takes effect at once and is logged. It is not yet an event bus that HR or security tooling can publish to.

### 6.4 Signing keys

Each node holds its own ES256 key on local disk and publishes the public half at `/.well-known/mif/jwks.json`. A node accepts snapshots signed by itself and by the peers whose public keys are in its `trusted/` directory. Exchanging those public keys between the Azure node and the on-prem standby is a one-time setup step.

## 7. Detecting the outage

The node probes Entra the way a sign-in uses it: the discovery document, the signing keys, and optionally a real token request made with a dedicated app registration that has no permissions.

| Setting | Suggested start | Effect |
|---|---|---|
| Probe interval | 15 s | |
| Failure threshold | 4 | Degraded after about one minute of consecutive failures |
| Recovery threshold | 8 | Normal after about two minutes of consecutive successes |

A flapping upstream does not change the mode, because both edges need an unbroken run.

The probes see Entra from the node. They do not see what users see. A conditional-access misconfiguration or an MFA outage can lock people out while every probe passes. For those cases an operator **forces** degraded mode on the node, with a reason that is written to the audit log. The same override runs a drill.

## 8. Token profile

Schema: [`schemas/mif-token-claims.schema.json`](../../schemas/mif-token-claims.schema.json).

| Claim | Meaning |
|---|---|
| `sub` | Stable subject at the node, identical in both modes |
| `roles` | Roles as of the last live verification |
| `mif_idp` | The upstream that verified this session live. **Absent in a continuity session.** |

Applications decide with one rule: *no `mif_idp`, no high-risk operations*. The rule fails closed, so a misconfigured token is treated as continuity, never as live. For ASP.NET Core this is the `Mif.LiveIdentity` policy in `Mif.Client`.

The spec (§22) requires degraded tokens to carry the `offline_fallback` scope and `degraded: true`. v0 does not emit them: the absence of `mif_idp` stands in for both. Producing them truthfully per session needs a custom Keycloak mapper, which is listed under next steps.

## 9. Azure node and on-prem standby

The standby is cold: its Postgres replica runs continuously, and its broker and node are started at failover.

- **Replicated:** the Postgres database. It holds the broker's realm, users, passkeys and realm keys, and the node's `mif` schema with snapshots, revocations, mode and audit log.
- **Not replicated:** the snapshot signing key. Each node has its own (§6.4).

Failover is manual in v0:

1. Promote the on-prem Postgres replica.
2. Start the broker and `mif-node` on-prem.
3. Point the issuer hostname at the on-prem node on the mesh.

| Failure | Result |
|---|---|
| Entra sign-in down | Azure node enters degraded mode. Passkey sign-in works. |
| WireGuard hub down, VM up | Clients reach the Azure node over NetBird. |
| Azure VM or region down, Entra up | **Applications behind the node are unreachable until failover**, although Entra is healthy. |
| Azure and Entra both down | Fail over to on-prem, which comes up in degraded mode. Reachable over NetBird. |
| On-prem down | No effect until a failover is needed. |

The third row is the cost of this design: applications that trust the node depend on the node. Put only the backup services behind it at first, and treat the standby as part of the system, with a failover drill, not as an afterthought.

## 10. Security notes

- The node's service account can change sign-in flows and end sessions in the realm. Its secret is as sensitive as a realm administrator's.
- The operator endpoints have no login, by design: operating the node must not depend on the identity system being healthy. They are reachable only on the loopback port, over SSH.
- A stolen device with an unlocked passkey gives continuity access until the snapshot expires or an operator revokes the user. Require user verification on the passkey (PIN or biometric), which this profile does.
- The prototype runs Keycloak in development mode over plain HTTP with throwaway secrets. None of that configuration is for deployment.

## 11. Gaps against the spec

The spec marks several things as not optional for production. This is where v0 stands on each.

| Spec requirement | v0 |
|---|---|
| `offline_fallback` scope and `degraded: true` on degraded tokens (§13.2, §22) | **Not met.** Absence of `mif_idp` carries the same meaning and fails closed, but it is not the spec's signal. |
| Applications deny privileged operations by default in degraded mode (§12.3) | Met in the demo portal through the `Mif.LiveIdentity` policy. There is no central policy engine (OPA). |
| Out-of-band kill switch, revocation applied within 60 seconds (§15.4) | **Partly met.** Manual revocation on the node is immediate and independent of Entra. There is no event bus. |
| Degraded tokens live at most 30 minutes (§13.2) | Met: access tokens live 5 minutes. A degraded session itself lasts until it idles out or reaches its maximum age, the snapshot expires, an operator revokes it, or recovery. |
| All degraded sessions invalidated at recovery (§13.5) | Met at the broker: the node ends every broker session. An application's own session lasts until it expires (one hour in the demo portal) and stays restricted for that time. |
| Audit entry for every session active during degraded mode (§13.5) | **Not met.** Mode changes, snapshots, revocations and the session sweep are logged; individual degraded sign-ins are only in the broker's own event log. |
| Two demo applications, low risk and high risk (§21) | One portal with one low-risk and one high-risk action. |

## 12. Next steps

**To pilot phase 1**

1. Register two applications in Entra: the broker (redirect URI `https://<issuer-host>/realms/mif/broker/entra/endpoint`, app roles for the roles you want mapped) and the probe (a secret, no permissions).
2. Run Keycloak in production mode with TLS on the Azure VM, import the realm with the Entra endpoints, and deploy `mif-node`.
3. Put one low-risk internal application behind the node and enrol passkeys for a pilot group.
4. Stand up the on-prem replica and exchange snapshot public keys.
5. Run a drill: force degraded mode, time how long people take to get in, then fail over to on-prem.

**Engineering follow-ups**

- A Keycloak mapper that emits the `offline_fallback` scope, `degraded` and `continuity_mode` per session, and an authenticator that verifies the snapshot signature at sign-in.
- A kill-switch consumer (Redis Pub/Sub or NATS) that feeds the node's existing revocation path, and an audit record for every degraded sign-in.
- Check that the user is still enabled in Entra when refreshing a snapshot, not only that they signed in.
- Per-application fallback policy held by the node, so the broker can refuse continuity sign-in to applications that must never allow it.
- Automated failover and a health-checked issuer hostname.

**Phase 2, communication backup**

- A self-hosted Matrix server on the mesh that signs people in through the node over OIDC.
- A contact directory (names, phone numbers, alternative addresses) synced from Microsoft Graph while it is healthy and readable in degraded mode.
- External mail is out of scope: while Exchange Online is down, inbound mail queues at the senders.
