# Common Web Platform - SaaS Readiness Discovery

Discovery date: 2026-11-14

## Executive Summary

The application has a useful tenant-aware local foundation, but the available project record does not establish a validated customer requirement for a hosted SaaS product. No production hosting provider, billing model, service-level target, tenant lifecycle policy, or support model has been approved.

The confirmed constraint is to avoid paid services for the current work. Therefore, the recommendation is to keep the application local and do not implement public SaaS hosting, payment integration, tenant self-service provisioning, or cross-tenant operator access until a customer use case, budget, and operational owner are confirmed. A free-tier host is not treated as a durable production plan.

This discovery is complete as a record of what is known, what remains unvalidated, and the gates required before implementation. It is not approval to deploy customer data or start billing work.

## Evidence and Confirmed Constraints

| Area | Finding | Evidence/status |
|---|---|---|
| Product direction | The project is intended to be both a reusable framework and a possible multi-tenant SaaS. | Product direction is recorded in the PRD and ADR-001. |
| Customer demand | Earlier discussion requested a demo client with all services, but no signed customer requirement, target deployment, workload, service level, data-residency requirement, or willingness-to-pay evidence is recorded. The demo tenant/client is local demonstration data, not evidence of SaaS demand. | Unvalidated; must be confirmed with the user and prospective client. |
| Current hosting | Local Windows development with SQL Server LocalDB, the API, and React; the startup script is for local use. | Documented in LocalDevelopment.md. |
| Cost constraint | Avoid paid services for the current workflow. | Confirmed in project history and ADR-003. Whether a future client can fund production hosting is unanswered. |
| Tenant foundation | Tenant membership, role-based permissions, tenant-scoped records, audit, reports, and cross-tenant integration tests exist. | Implemented foundation; not a SaaS operations capability by itself. |
| Tenant administration | No production operator API for tenant provisioning, suspension, closure, export, or deletion is documented. | Gap; do not infer lifecycle support from tenant IDs or seeded records. |
| Billing | No plans, pricing, quota enforcement, subscription lifecycle, invoices, payment processor, or billing records are implemented. | Explicitly deferred pending demand and hosting decisions. |
| Operations | Development health, local migrations, local storage/outbox, and audit capabilities exist. Production TLS, managed secret/key operations, backups/restore, monitoring/alerts, incident response, and support access controls have not been selected or exercised. | Production readiness is unproven. |

## Client-Demand Discovery Required

Before selecting a SaaS deployment or commercial model, confirm at least one real prospective customer's:

- Business problem and exact user workflow the platform must support.
- User and tenant count now and expected growth over the next year.
- Required availability, support hours, recovery time, and acceptable data loss.
- Data categories, jurisdiction/residency, retention, export, and deletion needs.
- Authentication needs, including whether local accounts are acceptable or SSO is required.
- Integrations, file volume, email/notification expectations, and data import needs.
- Budget owner, acceptable recurring spend, and whether the customer expects a paid SaaS subscription or a custom deployment.

Current conclusion: these facts are not in the project record. Treat demand as unvalidated rather than assuming the demo client represents a customer commitment.

## Hosting and Cost Decision

### Current recommendation

Continue local development and demos by default. Azure is the best researched candidate for a later isolated, disposable demo because Azure SQL Database uses the SQL Server engine and currently advertises a recurring free allowance. This is not a provider selection or deployment approval: do not create cloud resources until the user confirms an Azure subscription and approves the billing-safety configuration. Do not place real client data on an internet-accessible environment. The current LocalDB setup and local adapters do not provide production availability, backup, monitoring, or recovery guarantees.

### Options to assess after requirements are known

| Option | Potential fit | Required validation |
|---|---|---|
| Azure App Service Free (F1) + Azure SQL Database free tier | Closest researched match for the current .NET 10 API and SQL Server/EF Core stack. Microsoft's current SQL Database free-tier page advertises 100,000 vCore-seconds, 32 GB data and 32 GB backup storage per database per month, with up to 10 free-tier databases per subscription. | App Service Free uses shared compute with CPU quotas and cannot scale out; the SQL free tier has no SLA. Configure the SQL database to pause at its free limit rather than allowing paid overages. Confirm subscription/region eligibility, runtime availability, and that no supporting resource or later scale-up can incur charges. Keep demo data synthetic and disposable. |
| Customer-managed/on-premises host | Could avoid a platform hosting bill where the customer already has maintained infrastructure. | Customer owns patching, network/TLS, backups, monitoring, restore tests, and support responsibility; confirm SQL Server licensing/edition and connectivity. |
| Self-host on existing developer/customer hardware | Suitable only for a controlled demo or non-critical pilot. | Reliable power/network, static/reachable ingress, TLS, firewalling, encrypted backups, key protection, patching, monitoring, and an identified on-call owner. A zero hosting invoice does not make this a supported SaaS service. |
| Third-party cloud or free tier | Could support a later public pilot if its capabilities and costs fit. | Provider terms, current free-tier limits and expiry, SQL Server compatibility, data region, egress/storage/backup costs, uptime, secrets, and a migration/exit path. Do not assume the free tier will remain free or meet production requirements. |

Azure's official documentation describes the [App Service hosting plans](https://learn.microsoft.com/en-us/azure/app-service/overview-hosting-plans) and the [Azure SQL Database free tier](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-tier?view=azuresql). Limits and eligibility can change; verify them again in the target subscription before creating anything. The database offer explicitly has no SLA and lets the owner choose whether to pause at the monthly free limit or allow paid overages. A free allowance for one resource does not make its supporting resources free.

The current React project is under `frontend-evaluation/react`, and the API enables credentialed CORS only in Development. A separate static frontend/API deployment therefore needs a reviewed production origin and cookie/CORS configuration; it is not ready to deploy as-is. Co-hosting or a same-origin setup may simplify this, but requires implementation and verification before a public demo.

No provider has been selected. Before any internet-facing deployment, record the owner, monthly budget ceiling, data region, availability/recovery targets, and operational responsibilities. If a truly zero-cost configuration cannot be guaranteed, pause deployment rather than risk a charge or weaken security controls.

## Tenant Lifecycle and Operator Controls

### Existing boundary

Tenant operations use an authenticated user's active membership and tenant-scoped permissions. This supports tenant-facing records but is not a controlled tenant lifecycle or operator console. Broad cross-tenant support access remains denied by default.

### Requirements to resolve before adding operator APIs

1. **Provisioning:** who may create a tenant, how its first owner is verified, and how accounts/memberships are invited.
2. **Lifecycle:** define `Active`, `Suspended`, and `Closed` behavior, including sign-in, API access, background work, and reactivation.
3. **Ownership and administration:** prevent accidental removal or suspension of the last tenant owner; define owner transfer and recovery.
4. **Offboarding:** define a tenant's notice period, data export, retention/legal hold, deletion authorization, backup expiry, and irreversible deletion procedure.
5. **Support access:** no standing unrestricted cross-tenant access. If support access is required, require just-in-time approval, a narrow scope and expiry, a reason, visible audit records, and review.
6. **Isolation evolution:** retain shared-database tenant IDs as the initial shape; evaluate database-per-tenant only when a client, regulatory, or operational requirement justifies its cost.
7. **Abuse and resource controls:** identify quotas/rate limits needed by actual usage and ensure a tenant cannot affect another tenant's availability.

These are decision requirements, not implemented behavior. Tenant creation, closure, export/deletion, and break-glass support must not be inferred from the current local seed/demo process.

## Billing and Entitlements

Billing automation is not required by any recorded client request and is not implemented. Do not add a payment SDK or payment-provider dependency at this stage.

If a validated customer later requires SaaS billing, first decide:

- Whether the product is subscription SaaS, a fixed-price custom deployment, or both.
- Who sets prices and handles tax, invoices, refunds, failed payments, and disputes.
- Which measurable limits (if any) vary by plan and how usage is calculated.
- Whether a tenant is suspended after non-payment, with what grace period and appeal path.
- Whether billing is manual/invoice-based initially or warrants payment automation.

Do not couple authorization to a future payment processor. Entitlements, if required, should be an explicit server-side capability with an auditable source of truth. Any paid processor or service requires explicit user approval and a documented total-cost assessment.

## Operational and Security Gates

Before onboarding real customer data, require an owner and evidence for each item:

- HTTPS and secure production cookie/CORS configuration; registration/account recovery and identity-provider decisions.
- Production-grade SQL Server hosting, least-privilege database credentials, secret rotation, and persistent protected Data Protection keys with controlled backup/restore.
- Automated database and file backups, encryption, retention, and a successful restore exercise with agreed recovery time and recovery point targets.
- Health/readiness checks, structured logs, actionable alerts, incident contacts, escalation, and an incident-response runbook.
- Security review of authentication, tenant isolation, uploads, webhooks/outbound requests, dependency updates, and administrative/support access.
- Migration deployment/rollback plan and staged rollout with no destructive migration against the only copy of customer data.
- Data classification, retention/deletion policy, export process, privacy notice/contractual responsibilities, and jurisdiction review.
- Capacity, rate limits, file/storage quotas, abuse response, and a tested upgrade/patch process.
- Clear operational owner for availability, support requests, backups, access reviews, and incident communications.

Existing authentication, audit, authorization, tenant-scoped reporting and integration tests are a foundation; they do not satisfy these production gates without deployment-specific configuration and operational exercises.

## Decision and Next Steps

1. Keep the product local/demo-only by default; no production hosting vendor, billing system, SaaS plan, or operator access model is approved. Azure is only a possible disposable-demo candidate.
2. The user must confirm a real prospective client and the required use case, operating budget (including whether any paid services are allowed), expected users/data, jurisdiction, and availability/support expectations.
3. After client discovery, create a deployment decision with a costed hosting option and named operational owner.
4. Define and approve the tenant lifecycle, data export/retention/deletion, and support-access policies before implementing operator controls.
5. Only add billing/entitlements if a validated business model requires them; keep payment integration as a separately approved decision.
6. Before production onboarding, close the operational/security gates above and perform a restore and incident-response exercise.

**PLAT-016 outcome:** discovery findings, known constraints, unknowns, and pre-implementation gates are documented. Decisions requiring real client input remain open by design; this document does not claim SaaS production readiness.

**PLAT-017 outcome:** assessed official Azure free-tier documentation against the current stack and recorded the quotas, cost risks, lack of SLA, and frontend/CORS blockers. No account, subscription, cloud resource, or public deployment was created. A demo deployment still requires explicit user approval and a verified no-overage setup.
