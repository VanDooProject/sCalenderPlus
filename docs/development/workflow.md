# Development Workflow

## 1. Conventional Commits

All commits on `main` follow [Conventional Commits 1.0](https://www.conventionalcommits.org/). Because we **squash-merge**, the **PR title** becomes the commit message and is what's validated.

```
<type>(<optional scope>)<!>: <description in imperative, lower case>

[body]

[footer: BREAKING CHANGE: …, Refs #123]
```

| Type | Use | Release effect (release-please) |
|---|---|---|
| `feat` | User-visible feature | minor (patch while < 1.0) |
| `fix` | Bug fix | patch |
| `perf` | Performance improvement | patch |
| `refactor` | No behaviour change | none |
| `docs` | Documentation only | none |
| `test` | Tests only | none |
| `build` | Build system, dependencies | none |
| `ci` | CI configuration | none |
| `chore` | Maintenance | none |
| `revert` | Revert a commit | patch |
| `feat!` / `BREAKING CHANGE:` | Breaking change | major (minor while < 1.0) |

Scopes (optional, enforced list): `api`, `core`, `worker`, `db`, `auth`, `perm`, `ical`, `caldav`, `import`, `billing`, `web`, `landing`, `ui`, `e2e`, `deploy`, `ci`, `docs`, `deps`.

Examples: `feat(perm): add event-level permission overrides`, `fix(ical): emit VTIMEZONE for every used TZID`, `docs: add product and architecture specification`.

Tooling: `commitlint` (`@commitlint/config-conventional` + scope enum) runs in CI on the PR title (`amannn/action-semantic-pull-request`) and locally via an optional `lefthook` commit-msg hook.

## 2. Branch naming

Human branches: `type/short-description` in kebab-case, type from the list above.

```
feat/event-permission-overrides
fix/ical-dst-shift
docs/permission-examples
chore/bump-dotnet-sdk
```

Optional issue number: `feat/123-event-overrides`.

**Exempt** (created by automation): `claude/*`, `release-please--*`, `dependabot/*`, `renovate/*`, `gh-readonly-queue/*`. A CI job `branch-name` checks the regex

```
^(feat|fix|perf|refactor|docs|test|build|ci|chore|revert)/[a-z0-9][a-z0-9._-]*$
```

and skips exempt prefixes.

## 3. Pull request process

1. Open an issue (or pick one from the roadmap); branch from `main`.
2. Small PRs (target < 400 changed lines excluding generated files). Draft PRs welcome early.
3. PR template: summary, linked issue (`Closes #…`), screenshots for UI, checklist (tests, docs, OpenAPI updated, migration added, i18n keys).
4. Required checks green (see §5). At least **1 approving review** (CODEOWNERS for `backend/src/SCalenderPlus.Core/Permissions/**` require the architecture owner — the permission engine is security-critical).
5. **Squash merge only**; PR title = commit message. Branch auto-deleted.
6. `main` is protected: no direct pushes, linear history, required checks, up-to-date branch (merge queue once traffic justifies it).

Database migrations: one EF migration per PR, named descriptively (`AddEventOverrides`); migrations must be backwards compatible with the previous release (expand → migrate → contract) because api replicas roll over gradually.

## 4. Releases with release-please

- `googleapis/release-please-action` on push to `main` maintains a **release PR** with changelog + version bump.
- **Single product version** (one `.release-please-manifest.json` entry, `release-type: simple`) for the whole monorepo: api, worker, web and landing ship together as one tested set. Version is written to `version.txt`, `backend/Directory.Build.props` (`<Version>`), and `frontend/app/package.json` via `extra-files`.
- Merging the release PR creates tag `vX.Y.Z` + GitHub Release → triggers `release.yml`: build and push images `ghcr.io/<owner>/scalenderplus-{api,worker,web,landing}:X.Y.Z` and `:latest`, then call Coolify deploy webhook for production.
- Every merge to `main` deploys to **staging** (images tagged `:main-<sha>`).
- Pre-1.0: `bump-minor-pre-major: true`.

## 5. CI pipeline (GitHub Actions)

`ci.yml` on `pull_request` and `push: main`. Jobs are path-filtered (`dorny/paths-filter`) but the required-check aggregator job (`ci-ok`) always runs.

```
            ┌──────────── lint-meta (pr title, branch name, markdown lint, actionlint)
            │
 trigger ───┼── backend ── build + format check + analyzers ─┬─ unit tests (Core, Application)
            │                                               ├─ integration tests (Postgres service / Testcontainers)
            │                                               └─ openapi export + diff check + oasdiff breaking
            │
            ├── frontend ─ install (pnpm cache) ─ lint (eslint, prettier) ─ typecheck (vue-tsc) ─ unit (vitest) ─ build app + landing
            │                                     └─ api-client regenerate + diff check
            │
            ├── e2e-mocked ── (needs frontend) Playwright vs built app with MSW (chromium, webkit, mobile viewport)
            │
            ├── docker ── build api/worker/web/landing images (no push on PR), trivy scan (HIGH/CRITICAL fail)
            │
            └── e2e-fullstack ── (needs docker) docker compose up (postgres, api, worker, web, mailpit, fake LLM)
                                  ─ run migrations ─ Playwright fullstack suite (chromium)
 ci-ok ── needs all ── single required status check
```

Other workflows:

| Workflow | Trigger | Purpose |
|---|---|---|
| `release-please.yml` | push main | release PR / tags |
| `release.yml` | tag `v*` | build+push images, deploy prod |
| `deploy-staging.yml` | push main (after ci) | push `:main-<sha>`, Coolify staging webhook |
| `codeql.yml` | weekly + PR | C# and JS/TS security analysis |
| `import-eval.yml` | manual / weekly | real-LLM evaluation of import corpus (secret `ANTHROPIC_API_KEY`) |
| Dependabot | weekly | nuget, npm, github-actions, docker; grouped minor/patch |

Caching: NuGet (`~/.nuget/packages` keyed by `Directory.Packages.props`), pnpm store, Playwright browsers, Docker buildx GHA cache.

## 6. Testing strategy

| Layer | Tooling | Scope | Runs |
|---|---|---|---|
| **Unit (backend)** | xUnit v3, FsCheck, Verify | Permission engine (table-driven from permissions.md examples + property tests), recurrence, TZ conversion, dedupe keys, entitlement rules, iCal projection (golden `.ics` files) | every PR, < 30 s |
| **Integration (backend)** | xUnit + `WebApplicationFactory` + Testcontainers Postgres (CI: same image), Respawn between tests | Endpoints end-to-end through EF/Postgres: authz on every endpoint (matrix test: each endpoint × each level), migrations apply from scratch, feed ETags, job queue SKIP LOCKED | every PR |
| **Unit (frontend)** | Vitest + Vue Test Utils + MSW | Composables, components (access badges, override editor), i18n key completeness | every PR |
| **E2E mocked** ("without backend") | Playwright against `vite preview` with `VITE_API_MOCK=1` (MSW in browser) | UI flows, error/edge states that are hard to produce for real (402 paywall, 412 conflict, 500), visual regression snapshots, a11y (`@axe-core/playwright`) | every PR, 3 browsers |
| **E2E full-stack** ("with backend") | Playwright against docker compose stack (Postgres, api, worker, web, Mailpit, fake LLM server) | Critical journeys: sign-up + email verify (Mailpit API), create group + invite, calendar + per-event override seen differently by two users, iCal feed download & assertions, import dry-run with fake LLM, paywall on limit | every PR (chromium), nightly (all browsers) |
| **Contract** | OpenAPI diff + oasdiff, typed MSW handlers | API compatibility | every PR |
| **Load** (later) | k6 | feed polling and event window queries | pre-release |

Rules:
- Every bug fix includes a regression test at the lowest possible layer.
- **Authorization matrix test is mandatory** for every new endpoint (a generated test enumerates endpoints from the OpenAPI document and fails if an endpoint has no authz test case).
- Coverage: Core ≥ 90 % lines (permission engine 100 % branches); no global coverage gate otherwise.
- Test data builders (`A.Calendar().OwnedBy(group).WithGrant(...)`) mirror the permissions doc vocabulary.
- E2E tests use `data-testid` attributes, never CSS structure; full-stack tests create their own users via a test-only seeding endpoint enabled only when `Testing__SeedEndpoint=true` (never in prod images' default config).

## 7. Local development

```
docker compose -f deploy/docker-compose.dev.yml up -d   # postgres + mailpit
dotnet run --project backend/src/SCalenderPlus.Api       # applies migrations in Development
dotnet run --project backend/src/SCalenderPlus.Worker
pnpm -C frontend install && pnpm -C frontend --filter app dev   # Vite proxies /api → :5080
pnpm -C frontend --filter app dev:mock                   # UI only, MSW mocks, no backend
```

Code style: `.editorconfig` + `dotnet format` (CI verify), nullable enabled, warnings as errors; ESLint flat config + Prettier; TypeScript `strict`.
