# React Evaluation Shell

This is the local React demo shell for the Common Web Platform sign-in, tenant overview and member-management experience. It is not a production-ready frontend; review the frontend decision in [DecisionLog.md](../../docs/MarkDowns/DecisionLog.md) and the hosting gates in [SaaSReadinessDiscovery.md](../../docs/MarkDowns/SaaSReadinessDiscovery.md).

## Run locally

Prerequisites: Node.js/npm and the API running in Development at `http://localhost:5247`.

From this directory:

```sh
npm ci
npm run dev -- --host localhost --port 5173 --strictPort
```

Open `http://localhost:5173`. Development CORS permits this origin. The shell uses the API's cookie session and obtains antiforgery tokens for state-changing requests. After loading a tenant, it displays dashboard counts and recent audit activity when the signed-in user has the report-read permission. The API remains authoritative for tenant membership and authorization.

## Verify

```sh
npm run build
npm run lint
npm audit --omit=dev
```

Set `VITE_API_URL` to override the default API base URL for local evaluation. Do not put credentials in the frontend configuration or commit local account details.
