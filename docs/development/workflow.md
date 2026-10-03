# Development Workflow

## 1. Conventional Commits

All commits on `main` follow [Conventional Commits 1.0](https://www.conventionalcommits.org/). Because we **squash-merge**, the **PR title** becomes the commit message and is what's validated.

```text
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
| `style` | Formatting only, no code change | none |
| `test` | Tests only | none |
| `build` | Build system, dependencies | none |
| `ci` | CI configuration | none |
| `chore` | Maintenance | none |
| `revert` | Revert a commit | patch |
| `feat!` / `BREAKING CHANGE:` | Breaking change | major (minor while < 1.0) |

Scopes (optional, enforced list): `api`, `core`, `worker`, `db`, `auth`, `perm`, `ical`, `caldav`, `import`, `billing`, `web`, `landing`, `ui`, `e2e`, `deploy`, `ci`, `docs`, `deps`.

Examples: `feat(perm): add event-level permission overrides`, `fix(ical): emit VTIMEZONE for every used TZID`, `docs: add product and architecture specification`.

Tooling: [`commitlint.config.mjs`](../../commitlint.config.mjs) (`@commitlint/config-conventional` + `types`/`scopes` enums) is the single source of the lists: the `pr-title` job in `.github/workflows/pr-checks.yml` reads them from it and validates the PR title with `amannn/action-semantic-pull-request` (scope optional but from the list, description starting lower case); locally usable via an optional `lefthook` commit-msg hook (not set up yet).

## 2. Branch naming

Human branches: `type/short-description` in kebab-case, type from the list above.

```text
feat/event-permission-overrides
fix/ical-dst-shift
docs/permission-examples
chore/bump-dotnet-sdk
```

Optional issue number: `feat/123-event-overrides`.

**Exempt** (created by automation): `claude/*`, `release-please--*`, `dependabot/*`, `renovate/*`, `gh-readonly-queue/*`. The `branch-name` job in `.github/workflows/pr-checks.yml` (pull requests only) checks the head branch against the regex

```text
^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)/[a-z0-9._-]+$
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
- Merging the release PR creates tag `vX.Y.Z` + GitHub Release. Because tags/releases created with the default `GITHUB_TOKEN` **do not trigger other workflows**, the image job runs inside `release-please.yml` gated on the action's `release_created` output (alternative: a GitHub App token so `release.yml` fires). It builds and pushes images `ghcr.io/<owner>/scalenderplus-{api,worker,web,landing}:X.Y.Z` and `:latest`, then call Coolify deploy webhook for production.
- Every merge to `main` deploys to **staging** (images tagged `:main-<sha>`).
- Pre-1.0: `bump-minor-pre-major: true` and `bump-patch-for-minor-pre-major: true` (matches the table in §1). XML/JSON `extra-files` use `x-release-please-version` markers or the `xml`/`json` updaters.

## 5. CI pipeline (GitHub Actions)

`ci.yml` on `pull_request` and `push` to any branch (until `main` exists the default branch is an automation branch, and pushes to feature branches get feedback before a PR is opened); a newer run for the same PR/ref cancels the older one. Jobs are path-filtered (`dorny/paths-filter`; changing `ci.yml` itself runs everything) but the required-check aggregator job (`ci-ok`) always runs: it fails if any job it needs failed or was cancelled, and treats skipped jobs as success. Required status checks: `ci-ok`, plus `pr-title` and `branch-name` from `pr-checks.yml` (pull-request-only workflow, re-runs when the title is edited).

```text
            ┌──────────── lint: markdownlint (.markdownlint-cli2.jsonc), actionlint   [pr-checks.yml: pr-title, branch-name]
            │
 trigger ───┼── backend ── build + format check + analyzers ─┬─ unit tests (Core, Application)
            │                                               ├─ integration tests (Postgres service / Testcontainers)
            │                                               └─ openapi export + diff check + oasdiff breaking
            │
            ├── frontend ─ install (pnpm cache) ─ lint (eslint, prettier) ─ typecheck (vue-tsc) ─ unit (vitest) ─ build app + landing
            │                                     └─ api-client regenerate + diff check
            │
            ├── e2e-mocked ── (parallel to frontend) Playwright vs Vite dev server in mock mode with MSW (chromium on PR; webkit + mobile viewport nightly)
            │
            ├── docker ── build api/worker/web images (landing from M7; linux/amd64, loaded, no push), trivy scan (fixable CRITICAL fail, HIGH/CRITICAL in job summary)
            │
            └── e2e-fullstack ── (needs docker; images handed over as artifact and `docker load`ed) docker compose up --wait
                                  (postgres, migrate, api, worker, web; mailpit, fake LLM from M6) ─ Playwright fullstack suite (chromium)
 ci-ok ── needs all ── single required status check
```

Other workflows:

| Workflow | Trigger | Purpose |
|---|---|---|
| `release-please.yml` | push main | release PR / tags |
| `release-please.yml` → release job | `release_created` | build+push images, deploy prod (see §4) |
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
| **E2E mocked** ("without backend") | Playwright project `mocked` against the Vite dev server in mock mode (`pnpm --filter app dev:mock` = `vite --mode mock`, MSW in the browser with the shared handlers from `@scalenderplus/api-client/mocks`) | UI flows, error/edge states that are hard to produce for real (402 paywall, 412 conflict, 500), a11y (`@axe-core/playwright`); visual regression snapshots only once the UI stabilises (post-beta) | every PR chromium, nightly 3 browsers |
| **E2E full-stack** ("with backend") | Playwright project `fullstack` against the docker compose stack (`deploy/docker-compose.yml --profile with-db`: Postgres, api, worker, web; Mailpit and fake LLM server when needed) at `E2E_FULLSTACK_BASE_URL` | Critical journeys: sign-up + email verify (Mailpit API), create group + invite, calendar + per-event override seen differently by two users, iCal feed download & assertions, import dry-run with fake LLM, paywall on limit | every PR (chromium), nightly (all browsers) |
| **Contract** | OpenAPI diff + oasdiff, typed MSW handlers | API compatibility | every PR |
| **Load** (later) | k6 | feed polling and event window queries | pre-release |

Rules:

- Every bug fix includes a regression test at the lowest possible layer.
- **Tenant isolation**: the authz matrix includes a cross-tenant case per endpoint (valid id from another user's calendar → 404), and an architecture test forbids reading events/calendars outside the permission-aware query service.
- **Authorization matrix test is mandatory** for every new endpoint (a generated test enumerates endpoints from the OpenAPI document and fails if an endpoint has no authz test case).
- Coverage: Core ≥ 90 % lines (permission engine 100 % branches); no global coverage gate otherwise.
- Test data builders (`A.Calendar().OwnedBy(group).WithGrant(...)`) mirror the permissions doc vocabulary.
- E2E tests use `data-testid` attributes, never CSS structure; full-stack tests create their own users via a test-only seeding endpoint enabled only when `Testing__SeedEndpoint=true` (never in prod images' default config).

## 7. Local development

```sh
docker compose -f deploy/docker-compose.dev.yml up -d   # postgres + mailpit
dotnet run --project backend/src/SCalenderPlus.Api       # applies migrations in Development
dotnet run --project backend/src/SCalenderPlus.Worker
pnpm -C frontend install && pnpm -C frontend --filter app dev   # Vite proxies /api → :5080
pnpm -C frontend --filter app dev:mock                   # UI only, MSW mocks, no backend
```

End-to-end tests live in `e2e/`, a standalone pnpm package with its own lockfile (Playwright is not a dependency of the frontend workspace, and the fullstack CI job needs no frontend install):

```sh
pnpm -C e2e install
pnpm -C e2e exec playwright install chromium             # once per Playwright version
pnpm -C e2e test:mocked                                  # starts `dev:mock` itself (port 5173, or E2E_MOCKED_PORT)
docker compose -f deploy/docker-compose.yml --profile with-db up -d --build --wait
pnpm -C e2e test:fullstack                               # against E2E_FULLSTACK_BASE_URL (default http://localhost:8080)
docker compose -f deploy/docker-compose.yml --profile with-db down -v
```

With a preinstalled Chromium of a different revision (e.g. a sandbox), set `PLAYWRIGHT_CHROMIUM_EXECUTABLE=/path/to/chrome` instead of installing browsers. Failed CI runs upload the Playwright report (and compose logs) as artifacts.

Unit tests reuse the same mock handlers: `useMockApi()` in `frontend/app/src/__tests__/msw.ts` starts an MSW node server; `server.use(http.get(...))` with the typed `http` from `@scalenderplus/api-client/mocks` overrides a response per test.

Database migrations (EF Core, in `SCalenderPlus.Infrastructure/Persistence/Migrations`):

```sh
dotnet tool restore                                       # dotnet-ef from dotnet-tools.json
dotnet ef migrations add <Name> --project backend/src/SCalenderPlus.Infrastructure \
  --startup-project backend/src/SCalenderPlus.Infrastructure --output-dir Persistence/Migrations
dotnet run --project backend/src/SCalenderPlus.Api -- migrate   # what the compose `migrate` service runs
```

Backend tests use Docker (Testcontainers Postgres) for tests marked `[Trait("Category", "Docker")]`. Without a Docker daemon, skip them with `SCAL_SKIP_DOCKER_TESTS=true dotnet test --solution backend` (reported as skipped) or exclude them with `dotnet test --solution backend -- --filter-not-trait "Category=Docker"`. CI always runs them.

Code style: `.editorconfig` + `dotnet format` (CI verify), nullable enabled, warnings as errors; ESLint flat config + Prettier; TypeScript `strict`.
