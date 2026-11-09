# Common Web Platform - Development Tracker

Last Updated: 2026-11-09

Project Status: Authenticated admin shell and member management complete; awaiting review

Overall Completion: 50% (8 of 16 backlog tasks done; Blueprint: 100% | Backend stack: 100% | Implementation: 6 of 14)

Database environment: SQL Server LocalDB `MSSQLLocalDB` and `sqlcmd` are installed. The instance was started and verified with a query returning the SQL Server name and product version. SSMS is installed at `C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE\Ssms.exe`; use it to manage the local database. SSMS is a client, while LocalDB is the running Database Engine.

Development environment: .NET SDK 10.0.300, .NET runtime 10.0.8, Node.js 24.15.0, npm 11.12.1, `sqlcmd`, and EF Core CLI 10.0.7 are available. The runtime is behind the current .NET 10 servicing patch and should be updated. The folder was not previously a Git repository and contained only the planning documents. `dotnet new` template initialization hit access denied under the default user profile; use a workspace-local `DOTNET_CLI_HOME` for CLI operations that need template state.

Repository orientation: See [README.md](./README.md) for the documentation map. Backlog task states and acceptance criteria are in [Backlog.md](./Backlog.md).

## Current Milestone: Secure Backend Foundation

Goal: Build tenant isolation, identity, membership and authorization on the verified SQL Server-backed API before frontend implementation.

Status: Complete

### Completed Planning Work

- [x] Record dual product direction: reusable foundation and multi-tenant SaaS.
- [x] Set incremental delivery, solo developer and local hosting constraints.
- [x] Define proposed modular monolith architecture, module catalog and initial tenant schema.
- [x] Create roadmap, prioritized task IDs, dependencies and acceptance criteria.
- [x] Add mandatory coding and security standards in `Rules.md` and link them from the project guide and Definition of Done.
- [x] Inspect the referenced Smart Fleet Markdown set and follow its descriptive headings, tables and checkbox tracker approach.
- [x] Reconcile backlog, roadmap and decision log for backend-first sequencing; preserve React/TypeScript and Blazor evaluation for after Swagger verification.
- [x] Confirm installed .NET, Node and SQL command-line tooling; record SQL Server connectivity and inspection limits.

### Next Actions

- [x] Start PLAT-002 backend stack and repository convention decision.
- [x] Complete PLAT-002 with documented backend versions, module boundaries and quality commands.
- [x] Complete PLAT-003 API host, Swagger/OpenAPI contract, error conventions and tests.
- [x] Start PLAT-004 SQL Server persistence and local database setup.
- [x] Complete PLAT-004 EF Core migration and local database configuration.
- [x] Complete PLAT-005 tenant context and tenant data access guard.
- [x] Complete PLAT-006 identity, membership and session lifecycle.
- [x] Complete PLAT-007 role, permission and authorization policies.
- [x] Start PLAT-001 frontend comparison after the backend authorization contract is documented and verified through Swagger.
- [x] Complete PLAT-001 with browser flow, accessibility and build/run comparison; record a proposed frontend recommendation.
- [x] Accept the React/TypeScript frontend recommendation after user review.
- [x] Complete PLAT-008 authenticated admin shell and member management; verify member lifecycle and role assignment.
- [ ] After user review, begin PLAT-009 audit records for security and administration actions.

## Progress Update Format

For each work session, append a dated heading with outcome, completed task IDs, verification performed and any blocker/next action. Keep percentages tied to accepted backlog tasks rather than estimates of undocumented work.

## Work Log

### 2026-11-09 — Backend-first sequence and environment inspection

- Outcome: moved frontend evaluation behind PLAT-003 Swagger verification; accepted the .NET 10 LTS / SQL Server / EF Core 10 backend direction and recorded local environment constraints.
- Task IDs: PLAT-002 Done.
- Verification: read all ten source-of-truth documents; inspected repository contents and tool versions; attempted `sqlcmd -S localhost -E` and confirmed the engine was unreachable. Reviewed dependency order and searched planning documents for conflicting frontend-first or PostgreSQL instructions. Service inspection was blocked by denied Service Control Manager access.
- Blocker / next action: PLAT-004 is unblocked by the reachable LocalDB instance. Update the local .NET runtime to a current servicing patch.

### 2026-11-09 — API foundation and SQL Server LocalDB verification

- Outcome: completed PLAT-003 with health and versioned sample endpoints, development-only Swagger UI/OpenAPI, correlation-aware validation/error responses and tests. Completed PLAT-004 with an isolated Tenancy module, SQL Server EF Core configuration, initial migration, readiness check, safe LocalDB configuration and the root startup script.
- Task IDs: PLAT-003 Done; PLAT-004 Done.
- Verification: LocalDB migration `20261109113651_InitialPlatformSchema` applied to `CommonWebPlatform`. The startup script launched the API; `/health` returned `ok`, `/health/ready` returned `ready`, `/openapi/v1.json` listed `/api/v1/sample`, and `/swagger/index.html` returned 200. Release build succeeded with 0 warnings/errors; 5 unit tests and 7 API/SQL Server integration tests passed; `dotnet format --verify-no-changes` passed; NuGet vulnerability audit reported no vulnerable packages.
- Environment note: NuGet restore used the online feed after elevated access was approved; local loopback tests and formatter also required elevated access because the sandbox denied socket/named-pipe operations. The installed .NET runtime remains 10.0.8 and should be updated to a current servicing patch.
- Next action: start PLAT-005 tenant context and data access guard using the SQL-backed Tenancy module. Keep production frontend implementation behind the backend identity, tenancy and authorization foundation.

### 2026-11-09 — PLAT-005 tenant isolation

- Outcome: verified tenant context resolution against an authenticated user's active membership and added tenant-scoped membership access.
- Task IDs: PLAT-005 Done.
- Verification: migration `20261109115451_AddTenantMemberships` applied to LocalDB; 8 unit tests and 8 API/SQL Server integration tests passed, including active/suspended membership, tenant A/B list/read/update/delete, guessed IDs and missing context; Release build had 0 warnings/errors; `dotnet format --verify-no-changes` passed.
- Next action: begin PLAT-006 with a maintained identity implementation and session lifecycle, then expose authenticated APIs through Swagger.

### 2026-11-09 — PLAT-006 identity and sessions

- Outcome: implementation started using ASP.NET Core Identity with SQL Server persistence and HTTP-only cookie sessions.
- Task IDs: PLAT-006 Done.
- Progress: added the Identity module and SQL Server user schema, development-only registration, sign-in/current-user/sign-out/revoke-all endpoints, fixed-lifetime cookie configuration, antiforgery validation, generic login failures, lockout and per-IP login limiting. Added isolated LocalDB API tests and registration-validator unit tests. Recorded ADR-009 and updated the security baseline.
- Progress: replaced the single-cookie registration with the complete ASP.NET Core Identity cookie set so security-stamp validation can sign out all relevant schemes. The two-client revoke-all test proves existing secondary sessions are rejected immediately.
- Verification: final Release verification passed: 11 unit tests and 12 API/SQL Server integration tests; formatter verification passed; build succeeded with 0 warnings/errors; OpenAPI/Swagger tests confirmed the auth paths are documented; NuGet vulnerability audit found no vulnerable packages. Applied `20261109120604_AddIdentityUsers` to local `CommonWebPlatform`; live smoke returned `ready`, Swagger HTTP 200, and auth routes in OpenAPI. The Identity module is included as a first-class solution project.
- Next action: begin PLAT-007 role, permission and authorization policies; keep frontend comparison deferred until the full required backend Swagger contract is complete and verified.

### 2026-11-09 — PLAT-007 role and permission policies

- Outcome: completed tenant-scoped roles and grants, permission policies, fail-closed authorization, and membership role assignment.
- Task IDs: PLAT-007 Done.
- Verification: 14 unit tests and 14 SQL Server/API integration tests passed; formatting passed; Release build succeeded with 0 warnings/errors; vulnerability audit found no vulnerable packages. Tests cover default authentication denial, permission denial/allowance after role assignment, repeated assignment idempotency, cross-tenant API rejection and the SQL Server composite foreign key. Applied migration `20261109122747_AddTenantRolesAndPermissions` to the local database; live smoke confirmed readiness, Swagger UI 200, role/membership paths in OpenAPI and 401 for unauthenticated role access.
- Next action: begin PLAT-001 frontend comparison now that the authenticated tenant and authorization API contract has been verified locally; keep frontend implementation connected only to these APIs.

### 2026-11-09 — PLAT-001 frontend comparison

- Outcome: built two equivalent local evaluation shells that call the verified API and started the side-by-side browser review.
- Task IDs: PLAT-001 In Progress (visual browser review not completed).
- Intended outcome: compare an equivalent sign-in, session and tenant-access shell against the existing API; assess accessibility, forms/tables, ecosystem, build/run and solo maintenance; document a recommendation while preserving both choices through the evaluation.
- Progress: created a React/TypeScript/Vite prototype and a standalone Blazor WebAssembly prototype with local registration/sign-in, cookie session, sign-out, tenant ID input and membership table. Added Development-only CORS for the two local origins. React dependency audit reported 0 vulnerabilities.
- Verification: React `npm run build` passed; Blazor restore/build passed with 0 warnings/errors; Release backend build passed. The Windows computer-use tool stopped because it could not determine the current browser URL with enough confidence to enforce policy, so visual browser comparison and API behavior from each browser shell remain unverified. All three temporary local servers were stopped.
- Next action: resume PLAT-001 after browser inspection is available; complete frontend/API flow checks, compare accessibility and maintenance tradeoffs, then record the selection before production frontend implementation.

### 2026-11-09 — PLAT-001 frontend comparison completed

- Outcome: completed the equivalent React and Blazor evaluation shells and recorded a proposed React/TypeScript recommendation in ADR-011. Both prototypes remain available pending user review; production frontend work has not started.
- Task IDs: PLAT-001 Done.
- Comparison: both shells completed browser sign-in, cookie session, tenant membership listing and sign-out against the local API. Both expose labeled credential and tenant fields, status/error announcements, and a captioned membership table; no horizontal overflow was observed at desktop, tablet or 390-pixel viewport widths. React's production JS bundle is 227.87 kB (70.97 kB gzip) and its prototype has a broad npm admin-component ecosystem, with a separate Node/TypeScript toolchain and duplicated API contracts as tradeoffs. Blazor retains a single C# language/toolchain but has a smaller admin-component ecosystem and a WebAssembly runtime download.
- Verification: React `npm run build`, `npm run lint`, and `npm audit` passed (0 vulnerabilities); Blazor build passed with 0 warnings/errors; API build passed with 0 warnings/errors. Browser verification confirmed both sign-in, tenant membership and sign-out flows. Labeled controls, announcements, table semantics and responsive widths were checked as a qualitative accessibility smoke test, not a formal WCAG audit. Fixed the Blazor evaluation page's always-visible default error banner; verified it stays hidden on normal signed-out startup and after sign-out.
- Next action: user review/approval of the proposed React recommendation. After approval, begin PLAT-008; do not start production frontend implementation before then.

### 2026-11-09 — PLAT-008 authenticated admin shell and member management

- Outcome: completed the React tenant member-management shell and tenant-scoped API flows. Administrators can add active registered accounts, suspend/reactivate memberships, and assign/remove tenant roles. The API rejects self-suspension, returns 404 for accounts or memberships outside the active tenant, and returns 409 for duplicate membership. Invitations remain deferred until PLAT-011.
- Task IDs: PLAT-008 Done.
- Verification: all 14 unit tests and 15 SQL Server/API integration tests passed; the new integration test covers member listing/detail, permission denial, registered/unknown/duplicate account handling, status changes, self-suspension rejection, cross-tenant IDs, and role assignment/removal. `dotnet format --verify-no-changes` passed; Release solution build succeeded with 0 warnings/errors; React production build and lint passed. Live `/health`, Swagger UI and OpenAPI returned 200 and OpenAPI listed the member and membership-status routes. The React preview is running at `http://localhost:5173`; the API is running at `http://localhost:5247`.
- Next action: user review of PLAT-008. Do not start PLAT-009 until explicitly approved.
