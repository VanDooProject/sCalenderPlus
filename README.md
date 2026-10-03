# sCalenderPlus

A shared calendar SaaS whose core idea is **different permissions for different events in the same calendar**, usable from any native calendar app via personalised iCal feeds.

> Status: early development (milestone M0 — scaffolding). The specification in [`docs/`](docs/README.md) is the source of truth.

## Repository layout

| Path | Contents |
|---|---|
| [`backend/`](backend/) | .NET 10 solution: Core, Application, Infrastructure, Api, Worker + tests |
| [`frontend/`](frontend/) | pnpm workspace: Vue 3 app, shared `ui` and `api-client` packages |
| [`e2e/`](e2e/) | Playwright end-to-end tests (mocked and full-stack) |
| [`deploy/`](deploy/) | Dockerfiles, compose files, Caddy configuration |
| [`docs/`](docs/README.md) | Product, architecture, development and deployment documentation |

See [docs/architecture/overview.md §2](docs/architecture/overview.md#2-repository-layout-monorepo) for the full layout.

## Getting started

Prerequisites: .NET SDK 10.0.1xx (pinned in [`global.json`](global.json)), Node.js ≥ 22.12, pnpm 10 (`corepack enable`).

```sh
# Local dependencies (PostgreSQL 17 + Mailpit)
docker compose -f deploy/docker-compose.dev.yml up -d

# Backend
dotnet build backend             # restore + build (warnings are errors)
dotnet test --solution backend   # unit + integration tests (Microsoft.Testing.Platform; integration tests need Docker)
dotnet run --project backend/src/SCalenderPlus.Api   # http://localhost:5080, migrates the dev database on start

# Frontend (pnpm workspace in frontend/)
pnpm -C frontend install
pnpm -C frontend lint            # ESLint + Prettier check
pnpm -C frontend typecheck       # vue-tsc / tsc
pnpm -C frontend test            # Vitest
pnpm -C frontend build           # production build of the app
pnpm -C frontend --filter app dev   # Vite dev server on :5173, proxies /api and /health to :5080
```

Production-like stack (api, worker, web, one-shot `migrate`, bundled PostgreSQL) from this checkout:

```sh
docker compose -f deploy/docker-compose.yml --profile with-db up -d --build
curl http://localhost:8080/health/ready          # served through web (Caddy) → api
docker compose -f deploy/docker-compose.yml --profile with-db down -v
```

More in [docs/development/workflow.md §7](docs/development/workflow.md#7-local-development).

## Documentation

- [Documentation index](docs/README.md)
- [Architecture overview](docs/architecture/overview.md)
- [Development workflow](docs/development/workflow.md) — conventional commits, branches, CI, testing
- [Deployment on Coolify](docs/deployment/coolify.md)
- [Roadmap](docs/roadmap.md)

## Contributing

Follow [docs/development/workflow.md](docs/development/workflow.md): Conventional Commit PR titles, small PRs, squash merge.

## License

To be decided (see open question 2 in [docs/roadmap.md](docs/roadmap.md) and issue #37). Until a `LICENSE` file is added, all rights are reserved.
