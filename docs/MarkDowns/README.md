# Common Web Platform

## Project at a Glance

Common Web Platform is a reusable application foundation that can be used as a framework for client projects and as a configurable multi-tenant SaaS product. The initial build is a modular monolith maintained by one developer and run locally.

## Documentation Map

| Document | Purpose |
|---|---|
| [PRD.md](./PRD.md) | Vision, users, scope and product boundaries |
| [Architecture.md](./Architecture.md) | System shape, stack recommendation and code layout |
| [Modules.md](./Modules.md) | Module catalog, ownership and delivery order |
| [DataAndSecurity.md](./DataAndSecurity.md) | Tenant model, schema outline, API and security rules |
| [LocalDevelopment.md](./LocalDevelopment.md) | Local setup, deployment and quality practices |
| [Rules.md](./Rules.md) | Coding, architecture and security rules required for development |
| [Roadmap.md](./Roadmap.md) | Incremental release plan and exit conditions |
| [Backlog.md](./Backlog.md) | Prioritized tasks, statuses, dependencies and acceptance criteria |
| [DecisionLog.md](./DecisionLog.md) | Recorded choices and open questions |
| [SaaSReadinessDiscovery.md](./SaaSReadinessDiscovery.md) | Client-demand findings and pre-production hosting, lifecycle, billing and operations gates |
| [Tracker.md](./Tracker.md) | Current status and work log |

## Working Agreements

- Keep this folder as the project source of truth; update the tracker when a task changes.
- Use task IDs from `Backlog.md`; use `Not Started`, `In Progress`, `Blocked` or `Done`.
- Deliver one end-to-end slice at a time, including tests and documentation.
- Follow [Rules.md](./Rules.md) for all implementation and review work.
- Record material architecture changes in `DecisionLog.md` and update affected docs together.
- The documents describe a recommendation, not a commitment to implement every module at once.

Project Status: PLAT-017 no-cost demo-hosting feasibility documented; no deployment approved

