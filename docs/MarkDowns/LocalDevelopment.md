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

Expose only local development ports by default. Compose is infrastructure convenience, not a requirement to containerize the application. Local email delivery is captured to console or a local sink. File storage uses a local adapter. No paid cloud service is needed for the initial workflow.

## Local Deployment Shape

For development, run the API and selected frontend separately with SQL Server installed locally. The repository-root `start-project.bat` checks prerequisites and starts the backend; after a frontend is added it will also start the frontend. It contains no credentials and cannot install or start a missing SQL Server Database Engine. If the engine is missing or unreachable, install/start SQL Server separately and use SSMS to confirm the server and database before retrying. For a later demo, build the frontend static assets and serve them through the API or a local reverse proxy; run one API process and one Microsoft SQL Server instance with persisted local data and backup instructions. PostgreSQL is not part of this project. Production deployment, TLS, managed secrets, monitoring and disaster recovery require a separate deployment decision before onboarding real customers.

### Startup Script

From the repository root, run `start-project.bat`. It checks for the .NET 10 SDK, `sqlcmd`, a reachable SQL Server Database Engine, and the local `CommonWebPlatform` database, then starts the backend API in a new console window. It gives setup guidance if SQL Server is unavailable or the migration has not been applied. The default server is `(localdb)\MSSQLLocalDB`; set `CWP_SQL_SERVER` for another instance and configure the matching `ConnectionStrings__PlatformDb` value in local environment settings. If the database is missing, run `dotnet ef database update --project src/Platform.Api/Platform.Api.csproj --startup-project src/Platform.Api/Platform.Api.csproj`. The script does not install prerequisites, create the SQL Server instance/database, provision credentials, apply migrations, or start a frontend. Keep credentials out of the batch file and source control.

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


