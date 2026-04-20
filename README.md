# Multi Identity Framework (MIF)

## Identity resilience for applications that can’t afford to stop

When your identity provider goes down, your applications don’t fail — **access does**.

* Users can’t log in
* Cross-organisation collaboration breaks
* Mobile devices lose sessions
* Critical services stop — even though they’re still running

**MIF exists to prevent that — safely.**

---

## What is MIF?

**Multi Identity Framework (MIF)** is an open **resiliency policy layer** for existing identity brokers (Keycloak, Authentik, Dex). It is not a new identity provider or replacement control plane.

MIF sits alongside your broker to provide:

* multi-provider **federation** policy
* **degraded-mode** access with enforced scope restrictions
* out-of-band **kill switches** for emergency revocation
* policy-driven **audit and recovery**

Applications continue to trust your existing broker. MIF governs what the broker may issue and permit during normal and outage conditions.

---

## The core idea

```
[ Applications ]
        ↓
[ Keycloak / Authentik / Dex (broker) ]
   ↑ policies, fallback rules, kill switches
[ MIF (Resiliency Policy Layer) ]
        ↓
[ Entra | Google | eID | Local Identity ]
```

MIF governs the broker.
The broker governs applications.
Existing provider integrations are preserved.

---

## The problem MIF addresses

Modern systems are increasingly dependent on a small number of identity providers.

This creates:

* ❌ single points of failure
* ❌ vendor lock-in
* ❌ fragile cross-org federation
* ❌ no safe degraded mode

When identity fails — everything above it fails.

---

## What MIF enables

* Multi-provider identity federation through existing brokers
* Controlled degraded access using explicit `offline_fallback` scope
* Deny-by-default policy for privileged and transactional actions during outages
* Out-of-band emergency revocation independent of the upstream IdP
* Full auditability of every continuity decision
* Mandatory post-recovery revalidation

---

## What MIF is NOT

* ❌ Not a replacement for Entra / Google / Okta
* ❌ Not a new identity control plane or token issuer
* ❌ Not a universal identity database
* ❌ Not a guarantee of real-time revocation during IdP outages
* ❌ Not a solution for downstream APIs that require native provider tokens (e.g. Microsoft Graph, Google Drive)
* ❌ Not a surveillance system

MIF is a **resiliency policy layer**, not an identity monopoly.

> **Downstream API caveat:** applications passing tokens to services that strictly expect native Entra or Google tokens (e.g. Microsoft Graph API) will still break during upstream outages. MIF does not eliminate this. OAuth 2.0 Token Exchange (RFC 8693) can bridge some cases but has its own outage-time limitations and must be treated as an optional, explicitly-configured integration.

---

## Degraded mode and the revocation trade-off

When MIF enters degraded mode it issues tokens with the `offline_fallback` scope. This scope signals to applications that:

* only low-risk, pre-approved operations are permitted
* privileged or transactional actions (financial approvals, password changes, admin writes) must be blocked at the application level
* the token may have been issued without live revocation confirmation

Applications **must** be updated to understand and enforce `offline_fallback`. This is not optional.

MIF does not hide the revocation limitation — it makes it explicit. The answer to “what if someone is terminated exactly as the IdP goes down?” is:

1. The kill switch / out-of-band event bus propagates the revocation to the MIF policy engine independently of the IdP.
2. Even if the kill switch has not yet fired, the `offline_fallback` scope ensures the session can only perform low-risk operations.
3. Post-recovery revalidation terminates all degraded sessions and forces re-authentication against the live IdP.

---

## Why now?

* Increasing reliance on cloud identity platforms
* Real-world outages impacting access (not systems)
* Cross-organisation identity fragility
* Regulatory and sovereignty concerns
* Growing need for operational resilience

---

## Example scenario

A service depends on a cloud identity provider.

A federation issue occurs.

* Users cannot authenticate
* Systems remain operational
* Services become inaccessible

With MIF:

* The policy engine detects the upstream outage and switches to degraded mode
* Existing sessions receive an `offline_fallback`-scoped token with a short expiry
* Applications in low-risk categories continue operating in read-only or restricted state
* High-risk actions (financial transactions, admin writes, password changes) are denied
* The kill-switch event bus remains active; emergency revocations are honoured even while the IdP is offline
* When the IdP recovers, all degraded sessions are invalidated and users must re-authenticate

---

## Core concepts

### Resiliency Policy Layer

MIF adds policy, continuity rules, and kill-switch integration on top of existing brokers without replacing them.

---

### Identity Snapshots

Signed, time-bound identity states used for continuity.

Not a cache — a **controlled fallback artefact** with explicit scope, assurance level, and short expiry.

> **Important:** snapshots do not provide real-time revocation guarantees. The policy engine must restrict snapshot-backed sessions to `offline_fallback` scope. The kill-switch event bus is the out-of-band mechanism for emergency revocation during outage.

---

### Continuity Modes

Explicit operating states:

* **Normal** — live upstream validation, standard token issuance
* **Degraded** — `offline_fallback` scope, restricted operations, short-lived tokens
* **Partitioned federation** — cross-org trust broken, local identity only
* **Emergency continuity** — broad failure, break-glass governance, critical services only

---

### Policy Engine

Controls:

* which services may operate in degraded mode
* what the `offline_fallback` scope permits per service
* maximum snapshot and degraded-token lifetime
* revalidation requirements on recovery
* kill-switch signal handling and revocation propagation

---

### Kill Switch / Out-of-Band Event Bus

An event channel that is **independent of the upstream IdP**. Used to push emergency revocation signals to the MIF policy engine during a partitioned federation state. This is the primary mitigation for the snapshot revocation gap.

---

## Smallest useful implementation (MVP)

MIF can start small.

Recommended starting point:

* Keycloak as the broker with MIF resiliency policies applied
* 2 identity providers (e.g. Entra + local)
* Snapshot service (short-lived, policy-scoped)
* OPA policy engine enforcing `offline_fallback` scope rules
* Kill-switch event consumer (e.g. Redis Pub/Sub or NATS)
* Two demo apps: one low-risk (read-only portal), one high-risk (admin/write)

Simulate provider outage → observe degraded-mode behaviour and scope enforcement

---

## Repository structure

```
mif/
├─ docs/
├─ schemas/
├─ examples/
├─ prototype/
└─ README.md
```

---

## Why this matters

Identity is becoming the **hidden dependency** behind everything:

* communication
* collaboration
* public services
* enterprise systems

When identity breaks, everything appears broken.

MIF introduces:

> **graceful degradation without abandoning existing identity providers or creating a new single point of failure**

---

## Reference model: Continuous Access Evaluation (CAE)

MIF’s design goals are intentionally aligned with [Microsoft’s Continuous Access Evaluation (CAE)](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation) and the emerging multi-vendor extension of that pattern.

CAE solves a similar problem by issuing long-lived tokens that can be explicitly revoked via continuous event signals — without requiring token expiry to enforce revocation. MIF aims to be a **multi-vendor, broker-mediated version of this pattern**, combining:

* continuous risk signalling (kill-switch event bus)
* degraded-scope enforcement (`offline_fallback`)
* snapshot-based continuity with explicit revocation limitations disclosed

---

## Call for feedback

This is an open discussion.

Looking for input on:

* architecture
* threat model
* trust model
* governance concerns
* privacy implications
* real-world applicability
* experience operating Keycloak / Authentik / Dex in high-availability scenarios

---

## One-line definition

**MIF is an open resiliency policy layer for identity brokers, enabling safe degraded-mode access during upstream IdP outages through explicit scope restriction, kill-switch revocation, and mandatory post-recovery revalidation.**

---

## Status

Early-stage concept and draft specification. Architecture review and critique actively sought.

---

## Contribute

Issues, discussions, and critiques are welcome.

Especially from:

* identity engineers
* security architects
* Keycloak / Authentik / Dex operators
* public-sector technologists
* anyone who has experienced identity outages
