# Common Web Platform - Decision Log

Record consequential choices with date, status and reason. `Proposed` entries are planning assumptions, not final selections.

| ID | Date | Decision | Status | Reason / follow-up |
|---|---|---|---|---|
| ADR-001 | 2026-11-08 | Product serves both reusable framework and multi-tenant SaaS use cases | Accepted | Agreed product direction. Begin as reusable modular monolith; SaaS operations follow client validation. |
| ADR-002 | 2026-11-08 | Plan all common modules, deliver incrementally | Accepted | Broad catalog with a solo-developer sequence; no expectation of simultaneous implementation. |
| ADR-003 | 2026-11-08 | Start with local hosting and avoid paid dependencies | Accepted | Local development is the initial operating mode; cloud hosting deferred until a client exists. |
| ADR-004 | 2026-11-08 | Use a modular monolith before considering microservices | Proposed | Reduces operational burden for one developer while preserving module boundaries. Reassess only with evidence. |
| ADR-005 | 2026-11-09 | Defer React/TypeScript vs Blazor evaluation until the backend auth, tenant and authorization Swagger contracts work locally | Accepted | Backend APIs and documented endpoints must be implemented and verified before frontend selection or implementation. Preserve both frontend candidates and the existing comparison criteria for the later evaluation. |
| ADR-006 | 2026-11-09 | Use .NET 10 LTS, SQL Server and EF Core 10 as the backend/data starting point | Accepted | The environment has .NET SDK 10.0.300, EF Core CLI 10.0.7, SQL Server LocalDB and SSMS 20. LocalDB is reachable and is the safe Windows-authenticated development default. PostgreSQL is excluded. SSMS is a management client, not the engine. Update the installed .NET runtime to a current servicing patch. |
| ADR-007 | 2026-11-08 | Begin multi-tenancy with shared database/schema and tenant IDs | Proposed | Use one local SQL Server database with a shared schema and tenant IDs; require explicit tenant context, comprehensive isolation tests and future reassessment. |
| ADR-008 | 2026-11-09 | Deliver and verify the backend Swagger/OpenAPI contract before frontend evaluation | Accepted | Use ASP.NET Core built-in OpenAPI generation and a pinned Swagger UI package in development only. Verify the secured authentication, tenant and authorization endpoints against SQL Server before frontend evaluation or implementation; use `dotnet format`, build and xUnit tests as the backend quality baseline. |
| ADR-009 | 2026-11-09 | Use ASP.NET Core Identity with HTTP-only cookie sessions for the initial identity lifecycle | Accepted | Keep identity in a separate module backed by the existing SQL Server context. Use 30-minute non-sliding cookies, immediate security-stamp checks, explicit logout/revoke-all, antiforgery tokens on state changes, generic login errors, lockout and per-IP login limiting. Enable local registration only in Development pending a production account-provisioning decision. |
| ADR-010 | 2026-11-09 | Use a global permission catalog with tenant-scoped roles and assignments | Accepted | Keep permission codes stable and global while roles, role grants and membership assignments include the verified tenant. SQL Server composite foreign keys enforce that assigned roles and memberships share a tenant. Authorization policies resolve permissions from active memberships on each request; authorization defaults to authenticated users, with explicit anonymous exceptions. |
| ADR-011 | 2026-11-09 | Recommend React/TypeScript for the admin frontend | Proposed | Both evaluation shells completed the same sign-in, session and tenant-membership flow. React offers a larger reusable admin-component ecosystem and a 71 KB gzipped prototype JavaScript bundle, while adding a separate Node/TypeScript toolchain and duplicated API contracts. Blazor keeps the UI in C# but ships a WebAssembly runtime and has a smaller component ecosystem. Recommend React for the upcoming admin shell; retain the Blazor prototype and wait for user review before PLAT-008. |

## Open Questions

- Does the user approve the proposed React/TypeScript recommendation before PLAT-008 begins?
- Is the first target client likely to require single sign-on, separate databases, or a specific deployment platform?
- Which two or three modules solve the earliest real client problem after identity and tenant foundations?
- Which identity provider and production account-provisioning flow best fit eventual SaaS deployment (external SSO, email verification and recovery remain undecided)?


