# React Evaluation Shell

This is a local comparison prototype for the Common Web Platform sign-in and tenant-membership experience. It is not the production frontend; review the proposed frontend decision in [DecisionLog.md](../../docs/MarkDowns/DecisionLog.md) before starting PLAT-008.

## Run locally

Prerequisites: Node.js/npm and the API running in Development at `http://localhost:5247`.

From this directory:

```sh
npm ci
npm run dev -- --host localhost --port 5173 --strictPort
```

Open `http://localhost:5173`. Development CORS permits this origin. The shell uses the API's cookie session and obtains antiforgery tokens for state-changing requests.

## Verify

```sh
npm run build
npm run lint
npm audit --omit=dev
```

Set `VITE_API_URL` to override the default API base URL for local evaluation. Do not put credentials in the frontend configuration or commit local account details.
