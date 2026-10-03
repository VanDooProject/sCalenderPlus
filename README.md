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
