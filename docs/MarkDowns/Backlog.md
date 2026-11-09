# Common Web Platform - Prioritized Development Backlog

Status values: `Not Started`, `In Progress`, `Blocked`, `Done`. Priority: `P0` release blocker, `P1` core, `P2` later, `P3` optional. Update status and dependencies as work changes.

| ID | Priority | Task | Status | Depends on | Acceptance criteria |
|---|---|---|---|---|---|
| PLAT-001 | P0 | Evaluate React/TypeScript vs Blazor admin shell | Done | PLAT-007 | After the backend authentication, tenant and authorization Swagger contracts work locally, assess the same small sign-in shell for accessibility, forms/tables, ecosystem, build/run and solo maintenance; preserve both options until then and record the recommendation. |
| PLAT-002 | P0 | Finalize backend stack and repository conventions | Done | None | Supported backend/runtime versions, SQL Server and EF Core choice, modular boundaries, formatting and test commands recorded in the decision log; implementation can proceed without choosing a frontend. |
| PLAT-003 | P0 | Establish backend API host, Swagger/OpenAPI and error conventions | Done | PLAT-002 | Locally runnable API exposes documented health and versioned sample endpoints in Swagger/OpenAPI; validation errors include a correlation ID; automated tests and build pass. |
| PLAT-004 | P0 | Add SQL Server persistence and reproducible local database setup | Done | PLAT-003 | Running SQL Server Database Engine is documented and reachable; EF Core migration applies; API uses safe local configuration; startup script reports missing engine clearly. |
| PLAT-005 | P0 | Implement tenant context and tenant data access guard | Done | PLAT-004 | Tenant resolved from authenticated membership; missing/invalid context denied; isolation integration tests cover list/read/write/delete and guessed IDs. |
| PLAT-006 | P0 | Implement identity, membership and session lifecycle | Done | PLAT-005 | SQL Server-backed ASP.NET Core Identity supports development registration and secure sign-in; CSRF-protected cookie sessions expire and revoke; login failures are generic and secrets are never logged or stored in plaintext. |
| PLAT-007 | P0 | Implement roles, permissions and authorization policies | Done | PLAT-006 | Server denies by default; role assignment is tenant-consistent; positive and negative permission tests pass. |
| PLAT-008 | P1 | Build authenticated admin shell and member management | Done | PLAT-007 | Admin can add an existing registered user by email, deactivate/reactivate their tenant membership, and assign/remove tenant roles. UI reflects but does not replace server authorization; email invitations wait for PLAT-011 notification delivery. |
| PLAT-009 | P1 | Add audit records for security and administration actions | Done | PLAT-007 | Actor, tenant, action, target, time and correlation recorded; secrets excluded; audit reads permission-controlled. |
| PLAT-010 | P1 | Add tenant settings module | Not Started | PLAT-009 | Tenant can manage validated non-secret settings; unique tenant/key constraint and authorization tests exist. |
| PLAT-011 | P1 | Add notification module with local delivery adapter | Not Started | PLAT-010 | Domain module can request notification via contract; local delivery visible without third-party account; failures are observable. |
| PLAT-012 | P1 | Add file metadata and local storage adapter | Not Started | PLAT-005 | Upload/download is tenant and object authorized, size/type limited, metadata persisted and storage path cannot escape configured root. |
| PLAT-013 | P2 | Add tenant-scoped dashboard and reporting foundation | Not Started | PLAT-009 | Reports query through supported interfaces; pagination/export permissions and tenant isolation covered. |
| PLAT-014 | P2 | Add webhook/integration foundation | Not Started | PLAT-011 | Subscriptions are permission-controlled; delivery signed, retried safely and attempts auditable. |
| PLAT-015 | P2 | Add workflow/approval module for a validated use case | Not Started | PLAT-007 | One real workflow has explicit states, authorization and audit; no generic workflow engine without evidence. |
| PLAT-016 | P3 | SaaS deployment, billing and operator controls discovery | Not Started | PLAT-013 | Client demand, hosting, tenant lifecycle, billing and operational/security requirements documented before implementation. |

## Backlog Rules

- A task is `Done` only when all acceptance criteria and Definition of Done in `LocalDevelopment.md` are met.
- Split any task that cannot be completed and verified as one manageable solo-development slice.
- Add IDs in sequence; do not reuse retired IDs. Record blocker and next action for blocked work in `Tracker.md`.
