# Common Web Platform - Coding and Security Rules

These rules apply to all application code, tests, configuration and documentation changes. Follow them during implementation and review. If a rule conflicts with a documented architecture decision, record the conflict and update the decision log before changing the standard.

## General Coding Practices

- Prefer simple, readable code with clear names and one responsibility per type or function.
- Follow the established language formatter, analyzer and repository conventions; keep changes focused.
- Avoid duplicated business rules, hidden side effects, speculative abstractions and unnecessary dependencies.
- Keep domain rules independent of HTTP, UI and database implementation details where practical.
- Use explicit types and validate external input at system boundaries.
- Handle errors deliberately; do not swallow exceptions or return success for failed operations.
- Make time, IDs and external services testable through explicit dependencies where needed.
- Keep public APIs and module contracts small, documented and backward-compatible where practical.
- Add comments only when they explain a non-obvious reason or constraint; keep documentation current with behavior.

## Architecture and Module Boundaries

- Keep the application a modular monolith unless an accepted decision justifies another deployment shape.
- Each module owns its domain rules, persistence mapping, API contracts and tests.
- Do not read or write another module’s tables directly; communicate through declared application contracts.
- Keep shared-kernel code minimal and business-neutral.
- Avoid introducing a dependency or module without recording its purpose and maintenance cost.
- Any cross-module or public contract change must update dependent documentation and tests.

## Security Rules

### Identity and Authorization

- Deny access by default; explicitly authorize every endpoint, operation and sensitive UI action.
- Treat the server as the security boundary. UI visibility is not authorization.
- Authenticate users through a maintained identity implementation; never implement custom password cryptography.
- Store passwords only as appropriately salted, slow password hashes through the identity provider/library.
- Protect session and refresh credentials; use secure, HTTP-only cookie settings where cookies are used, and rotate/revoke refresh credentials where supported.
- Apply least privilege. Separate platform-operator permissions from tenant administrator and member permissions.
- Require object-level authorization; possession of a record ID never grants access.

### Tenant Isolation

- Resolve the active tenant from an authenticated user’s valid membership; never trust a tenant ID supplied by the client without verifying membership.
- Require tenant context for tenant-owned operations and scope every query, update, delete, export and background task to that tenant.
- Check tenant ownership on both reads and writes; do not rely only on UI filters or ORM global query filters.
- Include tenant-aware uniqueness rules and relationships where applicable.
- Add negative tests proving one tenant cannot access another tenant’s records, including guessed IDs, search, aggregates, exports and indirect relationships.
- Treat platform support access as exceptional, least-privilege, time-limited where possible and fully audited.

### Data and Secrets

- Never commit passwords, API keys, signing keys, certificates, connection-string credentials or production data.
- Use ignored local secret storage or environment-specific secret management; provide safe placeholders in example configuration.
- Never log passwords, tokens, session identifiers, secrets or unnecessary personal/sensitive data.
- Collect and retain only data required for the feature; define access and retention for sensitive records.
- Use parameterized database access and EF Core query APIs; do not build SQL by concatenating untrusted input.
- Review migrations for data loss and tenant-boundary effects; back up important local data before destructive changes.

### Web and API Safety

- Validate request data server-side and enforce size, type and range limits.
- Encode output appropriately and avoid unsafe HTML/script rendering.
- Protect cookie-authenticated state changes against CSRF; configure CORS narrowly and explicitly.
- Use HTTPS outside local development; configure secure cookie flags in deployed environments.
- Return safe, stable error responses; do not expose stack traces, SQL details, secrets or internal infrastructure information to clients.
- Apply rate limits and abuse protections to login, password recovery, invitations, uploads and other sensitive operations.
- For uploads, validate authorization, size, allowed content and storage paths; do not trust the supplied filename or MIME type alone.
- For webhooks and integrations, verify signatures, constrain destinations, prevent replay where applicable and avoid server-side request forgery.

## Database and SQL Server Practices

- Use EF Core migrations as the reviewed source of schema changes; do not rely on undocumented manual production changes.
- Use SQL Server constraints and indexes to enforce important invariants in addition to application checks.
- Ensure tenant-owned uniqueness and lookup indexes include `TenantId` where appropriate.
- Use a least-privilege database account for application runtime; do not run the app as SQL Server administrator.
- Keep connection strings and credentials out of source control and logs.
- Review generated SQL and migration behavior for schema changes affecting tenant data or large tables.
- Maintain deterministic seed data for local development; never seed default production credentials.

## Testing and Review

- Test business rules, validation, authorization and failure paths, not only successful responses.
- Add integration tests for database mappings, migrations and tenant isolation using SQL Server-compatible behavior.
- Include regression tests for fixed defects and security findings.
- Before marking work done, run the repository’s documented formatter, analyzers, build and relevant tests.
- Review changed files for leaked secrets, overly broad authorization, cross-tenant access, unsafe logging and accidental data exposure.
- Do not claim a check passed unless it was run; record environment limitations and unresolved risks in the tracker.

## Required Review Questions

- Is the operation authorized on the server for this user, tenant, action and specific record?
- Can any query, export, background task or error path cross tenant boundaries?
- Is untrusted input validated, bounded and safely used?
- Could logs or errors reveal credentials, personal data or infrastructure details?
- Are database constraints, migration effects and rollback/backup needs understood?
- Are negative tests and documentation updated for the new behavior?
