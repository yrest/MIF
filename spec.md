# Multi Identity Framework (MIF) v0.1

## Draft open specification

**Status:** Draft for discussion  
**Intended audience:** identity architects, platform engineers, security teams, public-sector digital architects, resilience planners, standards contributors  
**Authoring intent:** open discussion document for OSS publication and expert review

---

## 1. Executive summary

Modern identity has become a concentration point of operational risk.

Across public and private sectors, core applications increasingly depend on a small number of upstream identity providers. When those providers degrade, misconfigure federation, suffer outage, revoke trust relationships, or become politically constrained, the downstream systems that rely on them degrade with them. In many cases, entire organisations lose access not because their own applications are broken, but because their identity dependency is unavailable.

The **Multi Identity Framework (MIF)** is a proposed architecture and open framework for **identity continuity, federation resilience, and provider abstraction**.

MIF does **not** attempt to replace every existing identity platform. It does **not** attempt to become a universal super-directory. Instead, it introduces a control plane above existing providers and authoritative systems so that applications trust a stable framework layer rather than trusting a single upstream provider directly.

At its core, MIF aims to provide:

- identity provider abstraction
- continuity during upstream provider outages
- controlled fallback and degraded operation
- trust-preserving, time-bound identity snapshots
- portability across public and private identity domains
- auditable and policy-governed federation

MIF is intended for environments where identity failure has consequences beyond inconvenience: government, healthcare, finance, regulated enterprise, critical infrastructure, and cross-organisation collaboration.

---

## 2. Problem statement

### 2.1 Current state

Most organisations consume identity as a platform dependency:

- enterprise identity via Microsoft Entra ID, Okta, Google, Ping, etc.
- workforce and citizen identity via national schemes, tax systems, sector portals, or local directories
- application authentication via direct trust in one external IdP

This produces convenience, but also concentration risk.

### 2.2 Failure patterns

Typical failure patterns include:

1. **Upstream outage**  
   The application is healthy, but login fails because the upstream identity provider is unavailable.

2. **Federation breakage**  
   Cross-organisation trust, metadata, certificates, or routing break, preventing collaboration between organisations.

3. **Policy drift or lockout**  
   A provider-side policy change, conditional access rule, device compliance check, or token configuration change disables legitimate access.

4. **Dependency lock-in**  
   Applications trust a specific vendor directly, making migration or parallel operation costly.

5. **Jurisdictional or political risk**  
   Critical access paths depend on foreign providers, external cloud infrastructure, or commercial incentives outside national control.

6. **Loss of continuity**  
   No safe degraded mode exists; identity failure becomes application failure.

### 2.3 Root issue

The root issue is not merely authentication. It is **over-centralised trust routing**.

Applications often bind directly to one provider as both:

- the source of truth
- the runtime trust anchor

This coupling turns identity into a single operational choke point.

---

## 3. Vision

MIF proposes a different model:

> Applications trust the framework.  
> The framework trusts and orchestrates multiple identity domains.

MIF provides a stable identity control plane that can:

- federate multiple upstream identity providers
- validate and normalise identity assertions
- issue its own signed framework tokens to downstream apps
- maintain controlled continuity during provider failure
- enforce policy around fallback, expiry, and assurance

The goal is not identity independence in the absolute sense. The goal is **identity survivability and optionality**.

---

## 4. Design principles

### 4.1 Provider abstraction, not provider denial

MIF assumes existing providers will remain in use. It should integrate with them, not require wholesale replacement.

### 4.2 Continuity over convenience

MIF prioritises service continuity under degraded conditions over perfect dependence on live upstream calls.

### 4.3 No blind cache semantics

MIF must not rely on naive copies of user state. Any continuity artefact must be signed, time-bound, policy-aware, and auditable.

### 4.4 Explicit trust boundaries

Every identity source, transformation, assertion, and issued token must have a clear chain of trust.

### 4.5 Policy-governed fallback

Fallback access must be deliberate and limited, not accidental.

### 4.6 Open standards first

MIF should use and extend open protocols wherever possible: OIDC, OAuth 2.0, SAML, SCIM, WebAuthn, X.509, JWT, JWS, JWE, mTLS.

### 4.7 Jurisdictional flexibility

MIF should allow deployment at different scopes:

- single organisation
- sector
- national
- supranational / EU

### 4.8 Auditability by default

Every continuity decision, assertion mapping, token issue, and fallback event must be logged in structured form.

---

## 5. Scope

### 5.1 In scope

- federation across multiple identity providers
- trust abstraction for applications
- continuity under provider outage or federation breakage
- signed, expiring identity snapshots
- policy-driven degraded access
- assurance classification
- identity linking across domains
- audit and observability
- reference architecture and implementation patterns

### 5.2 Out of scope for v0.1

- replacing national civil registries
- replacing all enterprise directories
- universal biometric identity
- global citizen identity scheme
- social identity products
- surveillance functions
- mass profile aggregation beyond explicit policy scope

---

## 6. Terminology

### Identity Source
An authoritative or semi-authoritative system that can assert identity facts, such as Entra ID, a national eID, a local directory, or an HR-rooted workforce identity store.

### Identity Domain
A bounded trust and policy environment with its own identifiers, authentication methods, and assurance model.

### MIF Node
A deployed framework instance that participates in identity validation, policy, token issuance, and continuity operations.

### Identity Snapshot
A signed, time-bound, policy-scoped representation of identity state suitable for controlled continuity.

### Assurance Level
A classification describing the confidence and provenance of an identity assertion.

### Fallback Mode
A controlled operating mode used when one or more upstream sources are unavailable or unreliable.

### Trust Anchor
A root cryptographic or institutional authority used to validate signatures and issue framework trust.

### Subject Link
A mapping between equivalent or associated identities across multiple domains.

---

## 7. Core architecture

## 7.1 Conceptual model

```text
[ Applications / Services ]
            ↓
[ Multi Identity Framework (MIF) ]
            ↓
[ Entra | Okta | Google | National eID | Local AD | Sector IdP ]
```

Applications trust MIF-issued assertions and tokens. MIF is responsible for:

- upstream federation
- claim validation and normalisation
- policy enforcement
- fallback logic
- audit generation
- token issuance

## 7.2 Main components

### 7.2.1 Federation Gateway
Handles inbound trust relationships with upstream providers.

Functions:
- OIDC and SAML federation
- metadata handling
- signing certificate validation
- provider routing
- upstream status tracking

### 7.2.2 Identity Normalisation Engine
Maps heterogeneous claim sets into a canonical MIF identity schema.

Examples:
- user principal names
- national identifiers
- assurance indicators
- group and role attributes
- organisation affiliations

### 7.2.3 Subject Linking Service
Associates identities across multiple domains under policy.

Examples:
- employee identity in Entra linked to local workforce ID
- citizen eID linked to service account or sector identity
- tax identity linked to business identity under legal rules

### 7.2.4 Policy Engine
Determines what is allowed under normal and degraded modes.

Examples:
- fallback duration
- access scope restrictions
- service sensitivity rules
- revalidation requirements
- revocation precedence

### 7.2.5 Snapshot Service
Produces and validates identity snapshots.

Properties:
- signed
- time-bound
- issuance context included
- assurance level included
- limited claims only
- revocation-aware where possible

### 7.2.6 Token Service
Issues MIF-native access and identity tokens for downstream applications.

MIF tokens should be:
- signed by MIF trust anchors
- short-lived where practical
- audience-scoped
- assurance-declared
- fallback-aware

### 7.2.7 Continuity Controller
Detects upstream failures and invokes approved fallback paths.

### 7.2.8 Audit and Observability Service
Produces structured logs, metrics, traces, and compliance artefacts.

### 7.2.9 Trust Anchor Management
Controls signing keys, rotation, certificate chains, and trust distribution.

---

## 8. Canonical identity model

MIF should define a canonical subject model independent of any single provider.

### 8.1 Subject fields

Minimum recommended fields:

- `mif_subject_id`
- `linked_subjects[]`
- `display_name`
- `primary_identifier`
- `alternate_identifiers[]`
- `organisation_context[]`
- `roles[]`
- `groups[]`
- `assurance_level`
- `authentication_methods[]`
- `source_authorities[]`
- `last_verified_at`
- `snapshot_expiry`
- `revocation_state`
- `policy_tags[]`

### 8.2 Linked subject example

```json
{
  "mif_subject_id": "mif:person:8c2f4a1b",
  "linked_subjects": [
    {
      "source": "entra",
      "subject": "a12345@org.example",
      "confidence": "high"
    },
    {
      "source": "national_eid",
      "subject": "eid:IE:99887766",
      "confidence": "high"
    },
    {
      "source": "local_directory",
      "subject": "EMP-44019",
      "confidence": "medium"
    }
  ],
  "assurance_level": "AAL2",
  "roles": ["finance-approver"],
  "policy_tags": ["regulated", "fallback-restricted"]
}
```

---

## 9. Identity snapshots

## 9.1 Purpose

Identity snapshots exist to support continuity, not convenience.

A snapshot is not a full profile copy. It is a signed continuity artefact with explicit lifetime, scope, and provenance.

## 9.2 Requirements

Each snapshot should include:

- canonical subject ID
- source provider(s)
- issue time
- expiry time
- assurance level
- restricted claim set
- policy scope
- signature
- issuer identity
- optional revocation handle

## 9.3 Properties

Snapshots must be:

- immutable once issued
- cryptographically verifiable offline
- non-replayable where practical
- short-lived by default
- policy-scoped

## 9.4 Example snapshot envelope

```json
{
  "iss": "mif-node.ie.gov.example",
  "sub": "mif:person:8c2f4a1b",
  "iat": 1776144000,
  "exp": 1776147600,
  "assurance": "AAL2",
  "fallback_scope": ["mail.read", "records.search"],
  "source_assertions": [
    "entra",
    "national_eid"
  ],
  "policy_tags": ["degraded-ok"],
  "continuity_mode": true,
  "jti": "c4e4fc17-7a1f-4b8e-9f22-2c0f3f8af911"
}
```

---

## 10. Trust model

## 10.1 Trust anchors

MIF requires explicit trust anchors.

Possible models:

- organisation-rooted trust
- sector-rooted trust
- national-rooted trust
- federated multi-root trust
- EU bridge trust model

## 10.2 Token trust

Applications trust MIF-issued tokens, not raw upstream assertions directly.

This allows:
- provider replacement
- multi-provider federation
- continuity semantics
- consistent downstream validation

## 10.3 Cross-domain trust

Cross-domain trust should be based on signed metadata, certificate distribution, and policy contracts rather than implicit transitive trust.

---

## 11. Assurance model

Not all identity assertions are equal.

MIF should classify assurance based on:

- source provider reliability
- authentication method used
- freshness of assertion
- whether live validation succeeded
- whether operation is continuity/fallback mode

### Example assurance bands

- `AAL0` — unknown / weak / self-asserted
- `AAL1` — basic authenticated identity
- `AAL2` — MFA-backed enterprise or eID-backed identity
- `AAL3` — high-assurance or hardware-backed verified identity
- `FAL1` — fallback using fresh signed snapshot
- `FAL0` — degraded continuity with restricted permissions only

Applications and policies should be able to require minimum assurance.

---

## 12. Policy model

## 12.1 Why policy is central

Without policy, continuity turns into uncontrolled stale access.

## 12.2 Policy dimensions

Policies should govern:

- which services can use fallback mode
- acceptable assurance levels
- maximum snapshot lifetime
- revalidation requirements
- role restrictions under degradation
- revocation precedence
- emergency override procedures
- human approval requirements for sensitive operations

## 12.3 Example policy rules

- tax filing portal may allow citizen login from fresh eID snapshot for 2 hours
- payroll admin cannot execute payment approvals in fallback mode
- healthcare records may allow read-only continuity but deny write operations
- inter-agency case management may require re-authentication once upstream provider returns

---

## 13. Continuity modes

MIF should explicitly support continuity modes rather than pretending all operation is equal.

### 13.1 Normal mode

- live upstream validation available
- standard token issue
- standard assurance

### 13.2 Degraded upstream mode

- one provider unavailable or degraded
- continuity based on recent signed snapshot or alternate provider
- operations limited by policy

### 13.3 Partitioned federation mode

- cross-org federation broken
- local organisation identity still available
- inter-org services may fail independently

### 13.4 Emergency continuity mode

- broad upstream failure
- break-glass governance
- restricted set of critical services only
- high audit requirements

---

## 14. Federation and interoperability

## 14.1 Protocols

MIF should support at minimum:

- OIDC / OAuth 2.0
- SAML 2.0
- SCIM for provisioning metadata
- WebAuthn for strong local auth where relevant
- mTLS for service-to-service trust

## 14.2 Provider adapters

MIF should expose pluggable adapters for:

- Entra ID
- Google Workspace / Cloud Identity
- Okta
- Keycloak
- national eID systems
- local Active Directory / LDAP
- sector-specific identity sources

## 14.3 Claim mapping

Claim mapping must be explicit and versioned.

Do not allow silent or ad hoc transformations.

---

## 15. Security considerations

## 15.1 Threat model

MIF is a high-value target.

Threats include:

- token forgery
- stale snapshot abuse
- replay attacks
- malicious provider assertion injection
- trust anchor compromise
- privilege escalation through subject linking
- revocation lag exploitation
- fallback mode abuse
- insider misuse

## 15.2 Security requirements

MIF should include:

- HSM-backed or equivalent signing key protection where practical
- mandatory key rotation
- signed metadata distribution
- nonce and replay protection
- device-bound or client-bound tokens where possible
- immutable audit logging
- strong admin separation of duties
- revocation propagation mechanisms
- continuous security monitoring

## 15.3 Snapshot safety constraints

Identity snapshots should never:

- become indefinite bearer credentials
- contain unnecessary personal data
- override revocation policy blindly
- substitute for high-risk live re-authentication in sensitive workflows

---

## 16. Privacy and civil liberties

MIF must be designed to resist becoming a surveillance consolidation layer.

### 16.1 Core privacy requirements

- data minimisation
- purpose limitation
- explicit policy scope
- separation between linkage and pervasive tracking
- no universal cross-context profiling by default
- transparent auditability
- controllable retention

### 16.2 Governance safeguards

Recommended safeguards:

- independent oversight
- published threat and privacy impact assessments
- transparent trust contracts
- reviewable policy logic
- restricted lawful access pathways

---

## 17. Governance model

## 17.1 Why governance matters

The main blockers to MIF are political, institutional, and commercial, not purely technical.

The framework should therefore be designed with governance models in mind.

## 17.2 Possible governance options

### Organisational MIF
Single enterprise runs MIF to reduce vendor dependency.

### Sector MIF
Healthcare, justice, finance, or education sectors run shared trust infrastructure.

### National MIF
A state-controlled but audited continuity framework bridges public and private identity domains.

### EU federation bridge
Member-state MIF systems interoperate through EU trust frameworks and eIDAS-compatible models.

## 17.3 Governance concerns

- who operates the trust anchors?
- who approves fallback policy?
- who is liable for continuity-issued access?
- how are citizens and organisations protected from overreach?
- how are commercial vendors integrated without capture?

---

## 18. Deployment models

## 18.1 Centralised control plane

One control plane with regional redundancy.

Pros:
- simpler policy
- simpler trust distribution

Cons:
- larger blast radius

## 18.2 Federated nodes

Multiple MIF nodes with shared trust contracts.

Pros:
- resilience
- jurisdictional separation
- policy autonomy

Cons:
- harder governance
- more complex consistency

## 18.3 Hybrid model

Recommended for most serious deployments.

- sector or national root governance
- distributed execution nodes
- shared trust metadata
- local policy enforcement

---

## 19. Example use cases

## 19.1 Government continuity

A government service portal uses MIF instead of trusting a single cloud IdP directly. If the upstream provider fails, citizens with fresh high-assurance identity snapshots can continue accessing low-risk services. Critical back-office functions remain available in controlled degraded mode.

## 19.2 Cross-organisation collaboration

Two organisations collaborate across federated identities. Their direct provider federation breaks, but MIF preserves continuity because both parties trust the MIF bridge and its cross-domain token format.

## 19.3 Regulated enterprise resilience

A finance organisation uses MIF to broker Entra, local AD, and national eID for specific regulated workflows. If Entra suffers outage, internal systems remain available through controlled fallback while high-risk operations remain blocked.

## 19.4 Public-private service interoperability

A tax-rooted business identity can be linked under policy to sector service access, allowing more robust cross-platform verification without requiring every application to integrate directly with the original authority.

---

## 20. Non-goals and anti-patterns

MIF should explicitly avoid the following anti-patterns:

### 20.1 Universal identity monopoly
MIF should not become a single global identity owner.

### 20.2 Endless stale session continuity
Continuity must be bounded and policy-limited.

### 20.3 Hidden federation magic
All mapping and trust transformation must be explicit.

### 20.4 Surveillance creep
Identity interoperability must not become behavioural centralisation.

### 20.5 Vendor-specific re-locking
MIF should not simply become a new proprietary lock-in layer.

---

## 21. Reference implementation direction

A practical reference implementation could be built in layers.

## 21.1 v0 prototype

Goals:
- prove the model
- show token abstraction
- demonstrate controlled fallback

Suggested components:
- Keycloak or similar broker as initial federation layer
- custom MIF policy engine
- canonical identity mapping service
- signed snapshot service
- MIF token issuer
- audit event stream

## 21.2 Prototype capabilities

- federate Entra + local IdP + one additional provider
- issue MIF tokens to sample apps
- simulate upstream outage
- switch to snapshot-based continuity mode
- enforce restricted fallback policy
- log every decision

## 21.3 Example prototype stack

- identity broker: Keycloak / Authentik / custom gateway
- policy engine: Open Policy Agent or custom service
- snapshot signer: dedicated signing service
- audit stream: append-only log / event store
- trust material: X.509/JWK set rotation
- demo apps: one low-risk portal, one high-risk admin app

---

## 22. Suggested token profile

MIF may define its own token profile on top of JWT/JWS or equivalent.

Recommended claims:

- `iss` — issuing MIF node
- `sub` — canonical MIF subject ID
- `aud` — intended application audience
- `iat` / `exp`
- `amr` — authentication methods reference
- `assurance`
- `continuity_mode`
- `source_assertions`
- `policy_scope`
- `linked_orgs`
- `jti`
- `snapshot_ref` (optional)

Applications should be able to make decisions based on continuity mode and assurance explicitly.

---

## 23. Observability and audit

MIF should treat observability as a first-class function.

### 23.1 Events to log

- upstream provider auth success/failure
- federation routing decisions
- subject linking decisions
- snapshot issue/validation/use
- token issue/revoke
- continuity mode transitions
- policy denials
- administrative changes
- trust anchor rotation

### 23.2 Log requirements

- structured format
- tamper-evident storage where practical
- privacy-aware retention
- cross-node correlation IDs

---

## 24. Open questions

The following require community review.

### 24.1 Trust root model
Should trust roots be national, sectoral, organisational, or bridged?

### 24.2 Revocation semantics
How should revocation be handled during prolonged upstream outage?

### 24.3 Assurance translation
How should assurance levels map between very different sources such as national eID, enterprise MFA, and local directory auth?

### 24.4 Subject linkage governance
Who approves identity linking across legal and organisational boundaries?

### 24.5 Citizen privacy
How do we support continuity and interoperability without creating identity super-profiles?

### 24.6 Emergency powers
Who can invoke emergency continuity mode, and how is abuse prevented?

### 24.7 Standardisation path
Should MIF be documented as an architectural pattern, protocol extension set, reference implementation, or formal standard?

---

## 25. Proposed roadmap

## Phase 0 — Discussion draft

- publish concept
- gather critique from identity experts, public-sector architects, and OSS communities
- refine terminology
- validate threat model

## Phase 1 — Reference architecture

- canonical identity schema
- token profile
- policy model
- continuity mode definitions
- trust model draft

## Phase 2 — OSS proof of concept

- minimal broker integration
- snapshot service
- policy engine
- sample applications
- simulated outage scenarios

## Phase 3 — Sector pilot

- one real organisation or consortium
- one low-risk service class
- one regulated service class
- external review and penetration testing

## Phase 4 — Standardisation discussion

- protocol profiles
- metadata standards
- assurance taxonomy
- governance templates

---

## 26. Why MIF matters

Identity is increasingly the hidden dependency behind access to everything else.

If identity fails, email fails. Collaboration fails. Portals fail. Back-office systems fail. Cross-organisation work fails. In some environments, national continuity becomes hostage to commercial platform health.

MIF is an attempt to introduce a different posture:

- not isolation
- not fantasy sovereignty
- not naive caching

But:

- controlled interoperability
- resilience against provider failure
- continuity with explicit trust
- policy-driven survivability

The framework is based on a simple premise:

> critical systems should not stop functioning merely because one upstream identity dependency is unavailable.

---

## 27. Call for feedback

This draft is intended as a discussion starter, not a finished doctrine.

Desired feedback areas:

- architectural flaws
- missing threat scenarios
- bad assumptions around governance
- privacy objections
- protocol compatibility concerns
- better assurance and continuity models
- existing projects or standards this should align with

---

## 28. One-sentence definition

**MIF is an open framework for resilient, policy-governed identity continuity across multiple identity providers and trust domains.**

---

## 29. Suggested OSS repository structure

```text
mif/
├─ README.md
├─ docs/
│  ├─ mif-v0.1-spec.md
│  ├─ architecture.md
│  ├─ threat-model.md
│  ├─ glossary.md
│  └─ use-cases/
├─ schemas/
│  ├─ canonical-subject.schema.json
│  ├─ identity-snapshot.schema.json
│  └─ mif-token-claims.schema.json
├─ examples/
│  ├─ sample-snapshot.json
│  ├─ sample-token.json
│  └─ sample-policy.rego
├─ prototype/
│  └─ ...
└─ LICENSE
```

---

## 30. Closing note

The technical path for MIF is challenging but manageable.

The harder problems are trust, governance, incentives, and power.

That is precisely why the idea is worth putting in front of practitioners now.

