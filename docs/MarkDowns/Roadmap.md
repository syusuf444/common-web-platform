# Common Web Platform - Roadmap

Roadmap stages are outcome-based; dates are intentionally omitted until task estimates and available hours are known.

## Stage 0: Backend Contract and Local Foundation

Deliver in dependency order: finalize backend stack and repository conventions; build and verify the API host, Swagger/OpenAPI contract, health and versioned sample endpoints; add SQL Server persistence and migrations once a Database Engine is available. Defer frontend selection and its React/TypeScript versus Blazor comparison until the Swagger contract works locally. Preserve both frontend candidates for that later evaluation.

Exit when: backend versions and conventions are recorded in `DecisionLog.md`, Swagger endpoints and their tests work locally, and the SQL Server Database Engine setup is documented. SSMS is used to manage the database but does not provide the Database Engine.

## Stage 1: Secure Foundation

Deliver: API host, persistence/migrations, tenant context, identity, membership, roles/permissions, admin shell and audit baseline.

Exit when: end-to-end sign-in and member administration work locally; cross-tenant negative tests pass.

## Stage 2: Reusable Core Modules

Deliver: settings, notifications (local adapter), file metadata/storage abstraction, shared UI patterns and module enablement approach.

Exit when: module contracts and integration tests show modules remain isolated and independently understandable.

## Stage 3: Operational Value

Deliver: dashboard/reporting, exports, integration/webhook basics, localization where validated by use cases.

Exit when: reports are tenant-scoped and export authorization is covered; operational documentation is current.

## Stage 4: SaaS Readiness

Deliver only after client validation: tenant provisioning, operator controls, billing/entitlements as needed, production hosting, backups, monitoring, security review and incident procedures.

Exit when: hosting and operational controls are selected, isolation/security review is complete, and a restore/incident exercise succeeds.

PLAT-016 discovery and PLAT-017 free-tier feasibility findings are recorded in [SaaSReadinessDiscovery.md](./SaaSReadinessDiscovery.md). Client demand and production budget/targets remain unvalidated, so the application stays local/demo-only by default. Azure is only a possible disposable-demo candidate; no account or resource has been created. Discovery does not authorize cloud deployment, SaaS operations, or billing implementation.

## Sequencing Rules

- Do not begin a module until its dependencies are complete or explicitly stubbed through a stable contract.
- Complete and verify the backend Swagger/OpenAPI contract before starting frontend evaluation or implementation.
- Keep API host/OpenAPI work independently runnable; database-backed work waits for a reachable SQL Server Database Engine.
- Keep each release small enough for one developer to build, verify and document.
- Revisit priorities after a real client use case; modules without demand remain planned rather than partially built.
