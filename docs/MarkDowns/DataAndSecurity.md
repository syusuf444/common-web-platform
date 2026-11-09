# Common Web Platform - Data, API and Security Design

## Initial Data Model

All primary keys use UUIDs unless a later measured need calls for another scheme. Store timestamps in UTC. Relational constraints and indexes are explicit. Every tenant-owned row includes `TenantId`; global reference data is marked and governed separately.

| Entity | Key fields | Scope / notes |
|---|---|---|
| Tenant | Id, Name, Slug, Status, CreatedAtUtc | Platform-level tenant record |
| User | Id, EmailNormalized, DisplayName, Status, CreatedAtUtc | Global identity; never imply tenant access from account existence |
| Membership | Id, TenantId, UserId, Status, JoinedAtUtc | Unique `(TenantId, UserId)` |
| Role | Id, TenantId, Name, IsSystem | Tenant-scoped role; reserved platform roles separate; unique `(TenantId, Name)` |
| Permission | Code, Description | Global stable permission catalog; tenant roles can grant only catalogued codes |
| RolePermission | TenantId, RoleId, PermissionCode | Unique composite key; role and permission foreign keys |
| MembershipRole | TenantId, MembershipId, RoleId | Composite foreign keys enforce role and membership belong to the same tenant |
| RefreshSession | Id, UserId, TokenHash, ExpiresAtUtc, RevokedAtUtc | Store token hashes, rotate and revoke; tenant association as needed |
| AuditRecord | Id, TenantId?, ActorUserId?, Action, ResourceType, ResourceId, OccurredAtUtc, CorrelationId, Metadata | Append-oriented, minimize sensitive metadata |
| TenantSetting | TenantId, Key, Value, UpdatedAtUtc, UpdatedByUserId | Non-secret values only; unique `(TenantId, Key)` |

Future module tables must declare tenant scope and include tenant ID in relevant uniqueness constraints, foreign keys where supported, and indexes. Avoid storing passwords directly; delegate credential hashing and security controls to a well-supported identity implementation.

## Multi-Tenancy Strategy

Start with a shared SQL Server database and shared schema with `TenantId` on tenant-owned records. `X-Tenant-ID` is only an explicit tenant selector; resolve it against an authenticated user's active membership before setting request tenant context. Missing context is denied for tenant operations. Membership list, read, update and delete queries include the verified tenant ID together with the requested record ID; writes bind the tenant ID from the context rather than the request body. Add database-level row security only as defense in depth after it is designed and tested. Never assume ORM filters alone prevent leaks.

Platform-operator data and tenant data have distinct authorization paths. Cross-tenant support access is denied by default, time-limited when enabled, explicitly permissioned and audited. Future enterprise deployments may require separate databases; keep tenant resolution abstract enough to evolve, without implementing database-per-tenant now.

## API Conventions

- Version public APIs under `/api/v1`; use resource-oriented REST and JSON.
- Use consistent plural resource names, standard HTTP methods/status codes and pagination for collections.
- Validate input at the boundary; return a stable error envelope with code, message, field errors and correlation ID.
- Publish OpenAPI; do not expose persistence entities as request/response contracts.
- Require authorization by default; anonymous endpoints are explicit allow-listed exceptions.
- Scope all tenant resource access by active tenant and verify object-level authorization.
- Evaluate tenant permissions from the active membership's assigned tenant roles on each request; role and membership IDs are always scoped by the verified tenant.
- Append security and administration events to the audit log with actor, tenant, action, target, UTC time and request correlation ID; audit metadata is limited to non-secret operational details.
- Require the dedicated `audit.read` tenant permission for the paginated audit endpoint; the migration grants it to existing roles that already have `roles.manage`. Audit records have no API update or delete operation.
- Manage ordinary, non-secret tenant settings under `/api/v1/tenant/settings`; `settings.read` and `settings.manage` are separate permissions, initially granted to existing roles with `roles.manage`. Values are plain text and limited to 2000 characters. Secret-like keys are rejected; do not store credentials, tokens, API keys, connection strings or cryptographic keys here. Setting values are never written to audit metadata.
- Send and inspect tenant notifications using the separate `notifications.send` and `notifications.read` permissions. The current local adapter writes plain-text content to a tenant-partitioned local outbox and does not send email; keep secrets and sensitive personal data out of notification bodies.
- Upload and read tenant files using `files.upload` and `files.read`. The local adapter stores files beneath a configured root using generated opaque names partitioned by tenant; metadata and file lookups are tenant-scoped. The API accepts only signature-checked PDF, PNG and JPEG files up to 10 MiB. Original names are metadata only and never form filesystem paths. Local storage does not provide malware scanning or production-grade object storage.
- Make retryable writes idempotent where duplicate execution would cause harm.

## Security Baseline

- Identity uses ASP.NET Core Identity backed by SQL Server. Its password hasher owns password storage; passwords are never stored or logged directly. Local account registration is exposed only in Development until a production provisioning and email-verification flow is designed.
- Browser sessions use the `CWP.Auth` HTTP-only, SameSite=Strict cookie with a 30-minute fixed lifetime and no sliding renewal. Production cookies require HTTPS. Security stamps are checked on every authenticated request, allowing password/account security changes and revoke-all to invalidate existing sessions immediately; logout clears the current cookie.
- Cookie-authenticated state-changing identity routes require an antiforgery request token in `X-CSRF-TOKEN`, paired with the HTTP-only `CWP.AntiForgery` cookie. Obtain a fresh request token after sign-in because tokens are bound to the current identity.
- Login uses a generic failure response, Identity lockout after repeated failed attempts, and a per-IP request limit. Do not log credentials, cookies, antiforgery tokens or password hashes.
- Audit successful/failed login, registration, logout, session revocation, member lifecycle and tenant role changes. Failed login audit records omit submitted email and password; tenant audit readers cannot read other tenants' rows.
- Enforce least privilege, deny by default, and check permissions server-side on every operation.
- Configure CORS narrowly.
- Validate and encode input/output; use parameterized ORM queries; apply upload limits and content validation.
- Keep secrets out of source control, logs, client bundles and ordinary tenant settings.
- Rate-limit authentication and sensitive endpoints; use generic login errors and audit security events without logging credentials/tokens.
- Apply migrations deliberately; back up local data before destructive schema changes.
- Add automated tests proving tenant A cannot read, mutate, delete or infer tenant B records, including by guessed IDs and list/filter/export paths.
