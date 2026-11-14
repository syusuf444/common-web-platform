# Common Web Platform - Module Catalog and Boundaries

## Delivery Rule

The catalog is the long-term product scope. Implement in the roadmap order, and release a module only when its authorization, tenant scope, API, UI, tests and documentation meet the shared quality bar. A module can be omitted from a client deployment through composition/configuration where practical.

## Foundation Modules

| Module | Owns | Dependencies | Initial status |
|---|---|---|---|
| Identity & Access | Users, credentials/external identity integration, roles, permissions, sessions | Tenancy | First |
| Tenancy | Tenant lifecycle, memberships, tenant context, tenant settings reference | None | First |
| Audit | Security and business audit records, actor and request correlation | Identity, Tenancy | First |
| Admin Shell | Navigation, access-aware menus, common UI patterns | Identity, Tenancy | First |

## Common Application Modules

| Module | Owns | Boundary notes | Planned stage |
|---|---|---|---|
| User & Organization Admin | Invitations, membership status, organization profile | Membership and role changes audited | Foundation |
| Settings & Feature Configuration | Tenant preferences, feature switches | Secrets remain in environment/secret store, never ordinary settings rows | Foundation |
| Notifications | Tenant-scoped notification requests and outbox, provider adapters | Local mode writes plain-text notifications to an inspectable file outbox; no external provider is contacted | Core |
| Files | Tenant-scoped file metadata and storage ports | Local adapter stores signature-checked PDF, PNG and JPEG files under a configured root | Core |
| Reporting | Tenant dashboard counts and paged audit reports | SQL-backed tenant queries; CSV export has a separate permission and bounded pages | Core |
| File Management | File metadata, ownership, access policy, storage interface | Binary storage adapter separate; validate type/size and authorize every download | Core |
| Dashboard & Reporting | Read models, saved filters, exports | No direct writes to module-owned data; exports tenant-scoped | Core |
| Workflow & Approvals | Tenant role access requests and approval decisions | Implement the validated role-access workflow first; avoid building a general BPM engine | Core |
| Search | Search contracts and module-owned indexing adapters | Add only when simple database queries are insufficient | Later |
| Integration/Webhooks | API credentials, webhook subscriptions, delivery attempts | Signed payloads, retries, replay controls and audit | Later |
| Localization | Locale preferences, translation resources, timezone display | Store timestamps in UTC; format at presentation edge | Later |
| Billing & Plans | SaaS subscriptions, quotas, plan entitlements | SaaS-only; defer until client demand and hosting model are known | SaaS stage |
| Platform Operations | Tenant provisioning, support access, platform health | Separate operator permissions and auditable break-glass access | SaaS stage |

## Shared Platform Services

These are host capabilities, not a dumping ground for business logic: configuration, structured logging, health checks, exception mapping, request correlation, OpenAPI, background-job abstraction when needed, and clock/time handling.

## Explicit Boundaries

- Identity authenticates a person; authorization evaluates tenant membership and permissions.
- Tenancy resolves active tenant context; tenant-owned modules require it on every operation.
- Audit records who did what and when; it does not become a general event store.
- Notifications deliver messages; the originating module owns the business decision to notify.
- Notifications exposes tenant-authorized send and outbox-read contracts. The current local adapter writes JSON records under the configured local outbox directory; it is a development/demo sink, not email delivery. Adapter errors are logged and surfaced to the API caller.
- Files stores metadata in SQL Server and content in the local adapter using generated names. File metadata and content access are permission-controlled and tenant-scoped; the original upload name is not used to construct a path.
- Reporting exposes tenant dashboard metrics and paged audit event reports through query interfaces. CSV export is separately authorized, excludes audit metadata and neutralizes spreadsheet formulas.
- Files owns metadata/access and storage adapters; domain modules retain their own business references.
- Reporting reads authorized module data through supported query interfaces; it does not bypass module rules.
