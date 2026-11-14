# Common Web Platform - Local Development and Quality

## Local Development

Prerequisites: the .NET 10 SDK, Git, EF Core CLI, and a locally installed/running Microsoft SQL Server Database Engine. The default local connection uses SQL Server LocalDB `(localdb)\MSSQLLocalDB` with Windows authentication; override it in local app configuration when using another instance. Use SSMS to create/manage the local database and inspect data. SSMS is a client tool and does not install the Database Engine. Node.js is required only after the React frontend is selected. Record the server/instance name and authentication mode in local setup notes; do not commit credentials.

1. Clone the repository and copy the documented example environment file to a local, ignored environment file.
2. Start SQL Server LocalDB with `sqllocaldb start MSSQLLocalDB` if needed; use SSMS to confirm the instance and inspect the development database.
3. Apply database migrations with `dotnet ef database update --project src/Platform.Api/Platform.Api.csproj --startup-project src/Platform.Api/Platform.Api.csproj`; run deterministic development seed data when available.
4. Start the backend first and verify its Swagger/OpenAPI endpoints locally. Review the React/TypeScript recommendation in [DecisionLog.md](./DecisionLog.md) before beginning frontend implementation; both frontend shells remain evaluation prototypes until the choice is approved.
5. In Development, obtain an antiforgery token from `/api/v1/auth/csrf`, register a local account at `/api/v1/auth/register`, then sign in at `/api/v1/auth/login`. For command-line requests, retain the antiforgery cookie from the token request and send the returned request token in `X-CSRF-TOKEN`:

   ```sh
   COOKIE_JAR=$(mktemp)
   CSRF_TOKEN=$(curl -fsS -c "$COOKIE_JAR" http://localhost:5247/api/v1/auth/csrf \
     | python -c "import json,sys; print(json.load(sys.stdin)['requestToken'])")
   curl -i -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
     -H "X-CSRF-TOKEN: $CSRF_TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"email":"your-account@example.local","password":"your-password"}' \
     http://localhost:5247/api/v1/auth/login
   rm "$COOKIE_JAR"
   ```

   The token and cookie must come from the same `/csrf` request; sending only the JSON body returns `antiforgery_validation_failed`. Fetch a fresh token after sign-in for subsequent state-changing auth requests. Registration is disabled outside Development; production provisioning, email verification and recovery are not implemented yet.

### Tenant member management

After signing in, send the active tenant ID in `X-Tenant-ID`. Member reads require `members.read`; adding members and changing membership status require `members.manage`; assigning or removing roles requires `roles.manage`. State-changing requests also need the authenticated `CWP.Auth` cookie, the paired `CWP.AntiForgery` cookie, and its request token in `X-CSRF-TOKEN`.

- `GET /api/v1/tenant/members` lists tenant members and assigned roles.
- `GET /api/v1/tenant/members/{membershipId}` reads one member; IDs from another tenant return `404`.
- `POST /api/v1/tenant/memberships` adds an existing active platform account with `{"email":"person@example.local"}`. An unknown account returns `404`; an existing membership returns `409`. Email invitations are not implemented.
- `PUT /api/v1/tenant/memberships/{membershipId}/status` accepts `{"status":"Suspended"}` or `{"status":"Active"}`. An administrator cannot suspend their own membership.
- `POST` and `DELETE /api/v1/tenant/memberships/{membershipId}/roles/{roleId}` assign and remove tenant roles; both require `roles.manage`.

The [Platform.Api.http](../../src/Platform.Api/Platform.Api.http) file contains request templates for these endpoints. Replace its tenant, membership, role, cookie and token placeholders with values from an authorized local session.

### Tenant file storage

Files are uploaded to `POST /api/v1/tenant/files` as multipart form data with a `file` field. Only PDF, PNG and JPEG content with matching signatures is accepted, with a 10 MiB per-file maximum. Upload requires the `files.upload` permission, an active `X-Tenant-ID`, authenticated cookies and a fresh `X-CSRF-TOKEN`. `GET /api/v1/tenant/files`, `GET /api/v1/tenant/files/{fileId}`, and `GET /api/v1/tenant/files/{fileId}/content` require `files.read`; metadata and content lookups are scoped to the verified tenant. Existing roles with `roles.manage` receive both file permissions through the migration. The local adapter stores generated opaque names under `%LOCALAPPDATA%\CommonWebPlatform\files`, partitioned by tenant UUID. Configure a different storage root with `Files:LocalStoragePath`. Original file names are metadata only and are never used as storage paths. This local adapter is not intended as a production malware scanner or content-disarm service.

Tenant audit records are available at `GET /api/v1/tenant/audit?skip=0&take=50` to members with the `audit.read` permission. Results are newest-first and restricted to the verified tenant; `take` is limited to 100. The audit feed records authentication outcomes, session revocation, member lifecycle and tenant role changes. Audit metadata is limited to non-secret operational details; failed login entries omit the submitted email and password.

### Tenant dashboard and reports

`GET /api/v1/tenant/dashboard` returns active/suspended member counts, stored file count, and audit event count for the last 30 days. `GET /api/v1/tenant/reports/audit?skip=0&take=50` returns a newest-first page of operational audit event fields without metadata. Both require `reports.read`, a signed-in user and a verified `X-Tenant-ID`. CSV export is available at `GET /api/v1/tenant/reports/audit/export.csv?skip=0&take=1000` and requires the separate `reports.export` permission. Exports are limited to 1000 rows per request; use `skip`/`take` to page. Existing roles with `roles.manage` receive both reporting permissions through the migration. Export fields are CSV-escaped and neutralized against spreadsheet formula injection.

### Local notifications

Tenant notifications can be written to and inspected in a local JSON outbox. `POST /api/v1/tenant/notifications` requires `notifications.send`, a valid tenant context, an antiforgery token, and a recipient, subject and plain-text body. `GET /api/v1/tenant/notifications?take=50` and `GET /api/v1/tenant/notifications/{notificationId}` require `notifications.read`. The new permissions are initially granted to existing roles that already have `roles.manage`. The default outbox is `%LOCALAPPDATA%\CommonWebPlatform\notification-outbox`, partitioned by tenant UUID. Override it with `Notifications:LocalOutboxPath` in local configuration. The adapter does not send email or contact a third party; failures are logged and returned as API errors. Avoid including secrets or sensitive personal data in demo messages.

### Tenant webhooks

Webhook subscription and delivery-history routes are in `Platform.Api.http`. Subscription management (`POST` and `DELETE /api/v1/tenant/webhooks/subscriptions`) requires `webhooks.manage`; listing subscriptions, reading a subscription and listing paginated deliveries with their attempts require `webhooks.read`. State-changing requests need the same authenticated cookies and antiforgery token as other tenant writes. The migration grants both permissions to existing tenant roles that already have `roles.manage`.

Before creating a subscription, configure an exact hostname in `Webhooks:AllowedHosts` (for example `Webhooks__AllowedHosts__0=hooks.example.com`). Endpoints must use HTTPS outside Development. Development permits HTTP only for loopback destinations; all destinations still require an allowlisted host. DNS results are checked before connecting, private/non-routable addresses are rejected, and redirects are disabled. Keep this allowlist controlled by the platform operator, not tenant input.

The generated signing secret is returned once and stored protected with ASP.NET Core Data Protection. Keep the Data Protection key ring persistent and backed up under the same application name (`CommonWebPlatform`); replicas must use the same protected key ring. Losing the key ring makes existing webhook secrets unreadable. Never include the one-time secret in logs or source control.

Each delivery is an HTTP POST with JSON content and `X-CWP-Webhook-Id`, `X-CWP-Webhook-Event`, `X-CWP-Webhook-Timestamp`, and `X-CWP-Webhook-Signature` headers. The signature is `v1=` followed by lowercase hexadecimal HMAC-SHA256 over the UTF-8 bytes of `<timestamp>.<raw request body>`, using the one-time Base64URL secret decoded to bytes. Consumers should compare signatures in constant time, reject stale timestamps, and deduplicate by delivery ID because retries reuse that ID. A request times out after 10 seconds; HTTP 408, 429, 5xx and network/timeouts retry with exponential delay, up to five attempts. Every completed attempt and its result is available from the tenant-scoped delivery-history endpoint.

Events are published internally through `IWebhookPublisher`; there is no public endpoint for tenants to inject arbitrary events. A producer module must explicitly publish an event for matching active subscriptions to receive deliveries. The default allowlist is empty, so outbound delivery remains unavailable until an operator configures a host.

### Role access approval workflow

An authenticated, active tenant member can submit `POST /api/v1/tenant/role-access-requests` with `{"roleId":"<tenant-role-uuid>"}` and review their own requests at `GET /api/v1/tenant/role-access-requests/mine?skip=0&take=50` or `GET /api/v1/tenant/role-access-requests/{requestId}`. Request lists are newest-first for the member and oldest-first for reviewers, with `skip`/`take` bounds matching other tenant reports. A duplicate pending request or a role the member already has returns `409`; roles from other tenants are not found. Members cannot approve or reject their own requests.

Members with `workflows.manage` can review `GET /api/v1/tenant/role-access-requests/pending` and decide with `POST /api/v1/tenant/role-access-requests/{requestId}/approve` or `/reject`. Approval assigns the requested role to the requester's still-active membership in the same transaction as the state change; rejection does not assign it. Requests transition once from `Pending` to `Approved` or `Rejected`. Both creation and decisions are recorded in the tenant audit log. The migration grants `workflows.manage` to existing roles that already have `roles.manage`; grant it to any other role that should review requests. All state-changing calls require the authenticated cookies and a fresh antiforgery token.

Expose only local development ports by default. Compose is infrastructure convenience, not a requirement to containerize the application. Notification requests are captured in the local outbox and do not send email. File storage uses a local adapter. No paid cloud service is needed for the initial workflow.

## Local Deployment Shape

For development, run the API and React development server with SQL Server installed locally. The repository-root `start-project.bat` starts or creates the default LocalDB instance, applies pending migrations, and launches the backend and frontend. It contains no credentials and cannot install a missing SQL Server Database Engine. If the engine is missing or unreachable, install it and use SSMS to confirm the server before retrying. For a later demo, build the frontend static assets and serve them through the API or a local reverse proxy; run one API process and one Microsoft SQL Server instance with persisted local data and backup instructions. PostgreSQL is not part of this project. Production deployment, TLS, managed secrets, monitoring and disaster recovery require a separate deployment decision before onboarding real customers.

### Startup Script

From the repository root, run `start-project.bat`. It checks for the .NET 10 SDK, Node.js/npm, EF Core CLI, and SQL Server command-line tools; starts or creates the default LocalDB instance when applicable; applies pending EF Core migrations to `CommonWebPlatform`; and opens separate consoles for the API and React development server. Open the frontend at `http://localhost:5173`, the API at `http://localhost:5247`, and Swagger UI at `http://localhost:5247/swagger`. On first run, it installs React dependencies with `npm ci` if `node_modules` is absent. For a non-LocalDB server, set `CWP_SQL_SERVER` and configure the matching `ConnectionStrings__PlatformDb` value in local environment settings; the script cannot start a separately installed SQL Server service. It does not install application prerequisites or provision credentials. Keep credentials out of the batch file and source control.

## Configuration

- Check in `.env.example` with names and safe placeholders only.
- Store credentials and signing keys in ignored local secrets or OS secret storage.
- Validate required configuration at startup and fail with a clear safe error.
- Separate development seed configuration from production configuration.

## Testing and Quality Standards

For backend changes, run these from the repository root when the solution exists:

```powershell
dotnet format --verify-no-changes
dotnet build
dotnet test
```

Run the relevant unit tests before marking each task done. Add SQL Server-backed integration/security tests for persistence, authorization and tenant isolation requirements. Record unavailable infrastructure and the exact failed check in `Tracker.md`; never report an unrun check as passed.

### Test Layers

- Unit tests: domain rules, validation and permission decisions.
- Integration tests: API, persistence mappings/migrations and tenant isolation against SQL Server behavior.
- Frontend tests: key forms, navigation/access states and error handling.
- End-to-end smoke: sign in, create/manage a tenant member and verify denied access.

### Definition of Done

Follow the mandatory coding and security requirements in [Rules.md](./Rules.md) for every change.

- Acceptance criteria pass and the feature is covered at the appropriate test layer.
- Authorization and tenant scope are explicit; negative access cases are tested.
- API/OpenAPI and user-facing behavior are consistent with conventions.
- Formatting, static analysis and build succeed locally.
- No secrets or sensitive data are logged; migrations and rollback/backup implications are reviewed.
- Docs and `Tracker.md` are updated; decision changes are recorded.

Keep CI free-tier compatible when introduced: restore, build, unit/integration tests and frontend checks. Do not make external paid services a test prerequisite.
