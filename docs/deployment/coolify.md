# Deployment on Coolify

Target: a self-managed [Coolify](https://coolify.io) v4 instance (Docker + Traefik), EU-hosted VPS. The same images and compose file also serve self-hosters outside Coolify.

## 1. Topology

| Coolify resource | Image | Domain | Notes |
|---|---|---|---|
| **PostgreSQL 17** | Coolify-managed database (`postgres:17-alpine`) | internal only | Coolify scheduled backups to S3. |
| **sCalenderPlus stack** (Docker Compose resource) | `deploy/docker-compose.yml` | | Services below. |
| └ `migrate` | `scalenderplus-api` with `migrate` command | – | One-shot, must succeed before api/worker start. |
| └ `api` | `scalenderplus-api` | internal (proxied by `web`) | Port 8080. |
| └ `worker` | `scalenderplus-worker` | – | No public port; health on 8081 internal. |
| └ `web` | `scalenderplus-web` (Caddy + SPA) | `app.example.com` | Port 8080 (non-root). Proxies `/api`, `/ical`, `/dav`, `/.well-known`, `/health`, `/openapi` to `api:8080`. |
| **Landing** (separate resource) | `scalenderplus-landing` (Caddy, static) | `www.example.com`, apex redirect | Deployed independently (marketing changes don't redeploy the app). |

Environments: **staging** (auto-deploy on `main`, image tag `main-<sha>`) and **production** (deploy on release tag `vX.Y.Z`), each a separate Coolify project with its own database.

Same-origin via `web` keeps cookies simple and hides the api container from Traefik. Alternative (Traefik path routing `app.example.com/api` → api directly) is possible, but Caddy in `web` makes the setup portable outside Coolify.

## 2. Images (`deploy/docker/`)

| Dockerfile | Build | Runtime |
|---|---|---|
| `api.Dockerfile` | `mcr.microsoft.com/dotnet/sdk:10.0` → locked `dotnet restore`, `dotnet publish -c Release` (framework-dependent, RID-neutral, no apphost, OpenAPI export off) | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, non-root `app` user (UID 1654), `ASPNETCORE_HTTP_PORTS=8080` |
| `worker.Dockerfile` | same SDK | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (needs Kestrel only for health endpoint), `ASPNETCORE_HTTP_PORTS=8081` |
| `web.Dockerfile` | `node:22-alpine` + pnpm (corepack, version from `frontend/package.json`) → `vite build` (app) | `caddy:2-alpine` with `deploy/caddy/web.Caddyfile`, non-root UID 10001 on port 8080 (the binary's `cap_net_bind_service` is removed so `no-new-privileges` works) |
| `landing.Dockerfile` | `node:22-alpine` → `vite-ssg build` | `caddy:2-alpine` with `landing.Caddyfile` |

All images: multi-stage, non-root, `HEALTHCHECK` defined, OCI labels (version, revision via build args `VERSION`/`REVISION`), pushed to GHCR. Build context is the repository root (`.dockerignore` keeps it small). The build stages run on `$BUILDPLATFORM` and produce architecture-neutral output, so `linux/amd64` and `linux/arm64` images build without emulation; CI (`ci.yml` job `docker`) builds `linux/amd64` only, loads the images, scans them with Trivy (fails on CRITICAL vulnerabilities that have a fix; HIGH/CRITICAL report in the job summary) and hands them to `e2e-fullstack`; multi-arch builds and pushes happen in the release/staging workflows. The SPA reads runtime config from `/config.json`, rendered by Caddy's `templates` directive from environment variables on each request (`deploy/caddy/config.json.tmpl`, whitelisted keys only, JSON-escaped) so one image works for staging and prod.

Chiseled images have no shell/curl, so container health checks for api/worker use a tiny built-in command: `dotnet SCalenderPlus.Api.dll healthcheck` (HTTP GET to localhost, exit 0/1). It probes `/health/ready` by default (`healthcheck live` probes `/health/live`); the port comes from `ASPNETCORE_HTTP_PORTS`, falling back to 8080 (api) / 8081 (worker), and `--url <base-url>` overrides both. Both hosts listen on those default ports when neither `ASPNETCORE_URLS` nor `ASPNETCORE_HTTP_PORTS` is set; since the aspnet base image sets `ASPNETCORE_HTTP_PORTS=8080`, `worker.Dockerfile` must set it to `8081`.

## 3. docker-compose.yml

[`deploy/docker-compose.yml`](../../deploy/docker-compose.yml) (variables in [`deploy/.env.example`](../../deploy/.env.example)):

| Service | Image / command | Notes |
|---|---|---|
| `migrate` | api image, `command: ["migrate"]` | One-shot, `restart: "no"`; waits for `postgres` healthy when the bundled database runs (`depends_on … required: false`). |
| `api` | api image | `depends_on: migrate: service_completed_successfully`; healthcheck `dotnet SCalenderPlus.Api.dll healthcheck` (`/health/ready`). |
| `worker` | worker image | Same `depends_on`; healthcheck `dotnet SCalenderPlus.Worker.dll healthcheck` on 8081. |
| `web` | web image | `depends_on: api: service_healthy`; healthcheck `wget http://127.0.0.1:8080/healthz`; published on `127.0.0.1:${WEB_PORT:-8080}` only (Traefik reaches the container port directly). |
| `postgres` | `postgres:17-alpine`, volume `postgres-data` | Only with `--profile with-db` (self-hosting, local, CI). Coolify uses a Coolify-managed database instead. |

Images are `${IMAGE_PREFIX:-ghcr.io/vandooproject}/scalenderplus-{api,worker,web}:${APP_VERSION:-local}`. The services also have `build:` sections: `up` pulls the tag and builds from the checkout only when it cannot be pulled (e.g. the default `local`). All app containers run `read_only` with a `tmpfs` `/tmp`, `cap_drop: [ALL]` and `no-new-privileges`. Environment shared by `migrate`, `api` and `worker`: `ConnectionStrings__Default` from `DATABASE_URL` (default: the bundled `postgres`), `App__PublicBaseUrl` from `APP_URL`, JSON console logs; `api` sets `Database__AutoMigrate=false`.

```sh
# Self-hosting / local / CI: bundled PostgreSQL
docker compose -f deploy/docker-compose.yml --profile with-db up -d --build
curl http://localhost:8080/health/ready        # through web → api
docker compose -f deploy/docker-compose.yml --profile with-db down -v
```

Coolify specifics: assign the domain to `web` only, port 8080 (Coolify's `SERVICE_FQDN_WEB_8080` magic variable), set `APP_URL` to that domain and `DATABASE_URL` to the Coolify-managed Postgres, mark secrets as "secret" in the UI, enable "connect to predefined network" so the stack reaches the Coolify-managed Postgres, and mark the one-shot `migrate` service `exclude_from_hc: true` so its exited state doesn't mark the stack unhealthy. `exclude_from_hc` is a Coolify-only key that plain `docker compose` rejects, so it is not in the shared file; it is added in Coolify's compose editor by the Coolify staging item, which also confirms that Coolify pulls the published tag rather than building from the `build:` sections.

**Token-bearing URLs**: Traefik access logs (if enabled on the Coolify server) and Caddy logs in `web` must not record `/ical/` paths in clear — disable access logs for that path or mask the token segment. `web.Caddyfile` logs JSON access logs with `request>uri` (and `Referer`) rewritten to `/ical/[REDACTED]` for every `/ical/` path; Caddy omits `Cookie`/`Authorization` values by default. Verified by the M4 log redaction test.

## 4. Environment variables

| Variable | Service | Required | Example / default |
|---|---|---|---|
| `ConnectionStrings__Default` | api, worker, migrate | ✓ | `Host=pg;Database=scal;Username=scal;Password=…;Maximum Pool Size=50` |
| `App__PublicBaseUrl` | api, worker | ✓ | `https://app.example.com` (feed links, emails) |
| `App__LandingUrl` | api | | `https://www.example.com` |
| `DataProtection__Keys` | api, worker | ✓ | stored in DB table (`PersistKeysToDbContext`) — no volume needed; key-encryption cert optional |
| `Auth__CookieDomain` | api | | empty (host-only cookie) |
| `Auth__External__Google__ClientId/Secret` (Microsoft, Apple) | api | | v1 |
| `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Password`, `Smtp__From` | worker (api for sync auth mails via queue) | ✓ | |
| `Billing__Provider` | api, worker | | `stripe` (SaaS) / `none` (self-host, default) |
| `Billing__Stripe__SecretKey`, `__WebhookSecret`, `__Prices__ProMonthly` … | api, worker | if stripe | |
| `Llm__Provider` | worker, api (dry run) | | `anthropic` / `openai_compatible` / `none` (default) |
| `Llm__ApiKey`, `Llm__Model`, `Llm__BaseUrl` | worker, api | if provider | |
| `Import__Enabled`, `Import__BlockedDomains` | worker | | `true`, `` |
| `Plans__SelfHost__*` | api, worker | | limit overrides |
| `WebPush__VapidPublicKey`, `__VapidPrivateKey` | api, worker | v1 | |
| `Database__AutoMigrate` | api | | `false`; `true` only in Development (see §7) |
| `Otel__Endpoint` | all backend | | OTLP endpoint (optional), e.g. `http://otel-collector:4317`; nothing is exported when unset |
| `Otel__Protocol` | all backend | | `Grpc` (default) or `HttpProtobuf` |
| `Logging__LogLevel__Default` | all backend | | `Information` |
| `Logging__Console__FormatterName` | all backend | | `json` (default outside Development: one JSON object per line, UTC timestamps) / `simple` |
| `Testing__SeedEndpoint` | api | | must be unset/false in prod |
| `API_UPSTREAM` | web | ✓ | `api:8080` (image default) |
| `WEB_PORT` | web | | listen port, `8080` (image default) |
| `PUBLIC_*` (e.g. `PUBLIC_ENVIRONMENT`, later `PUBLIC_SENTRY_DSN`) | web | | rendered into `/config.json`; each key must be added to `deploy/caddy/config.json.tmpl` (whitelist). `PUBLIC_ENVIRONMENT` → `environment` (default `production`) |

Compose-level variables (`deploy/docker-compose.yml`, see `deploy/.env.example`): `APP_URL` (→ `App__PublicBaseUrl`), `DATABASE_URL` (→ `ConnectionStrings__Default`), `POSTGRES_PASSWORD` (bundled database), `APP_VERSION` and `IMAGE_PREFIX` (image tags), `APP_ENVIRONMENT` (→ `PUBLIC_ENVIRONMENT`), `WEB_PORT` (host port).

All backend options are bound to typed options classes with `ValidateDataAnnotations().ValidateOnStart()` — a misconfigured container fails fast and Coolify keeps the old version running.

## 5. Volumes

- **None for app containers** (stateless). Data protection keys live in Postgres; uploads (later: attachments) go to S3-compatible storage (`Storage__S3__*`), not local volumes.
- Postgres data volume managed by Coolify.
- Caddy: no volume needed (TLS terminated by Traefik).

## 6. Health checks

| Endpoint | Meaning | Used by |
|---|---|---|
| `GET /health/live` | Process up (no dependencies). | Container healthcheck / restart policy. |
| `GET /health/ready` | DB reachable, migrations at expected version, required config valid. | Coolify/Traefik routing, deploy gating, uptime monitor. |
| Worker `:8081/health/ready` | DB reachable + job loop heartbeat < 2 min old. | Container healthcheck. |
| `web` `:8080/healthz` | Caddy static 200. | Container healthcheck. |

Health responses contain no secrets: only the overall status and each check's name and status (`{"status":"Unhealthy","checks":{"database":"Unhealthy"}}`), never descriptions or exceptions. HTTP 200 when healthy, 503 otherwise. Health requests are excluded from tracing.

## 7. Migrations

- **Separate one-shot `migrate` service** using the api image (`migrate` command → `context.Database.MigrateAsync()` under a Postgres advisory lock), run before api/worker start in every deploy. Rationale: never race migrations between multiple api replicas; failed migration = failed deploy, old containers keep serving.
- Migrations must be **expand/contract**: a release only adds (columns nullable/with defaults, new tables); removals happen one release later. This keeps the previous version working during rolling restarts and allows image rollback.
- Production also produces an idempotent SQL script artefact (`dotnet ef migrations script --idempotent`) attached to each GitHub Release for audit/manual application.
- In Development the api auto-migrates on startup for convenience; in Production `Database__AutoMigrate=false`.

## 8. Backups and restore

- Coolify scheduled Postgres backups: **every 6 h** (`pg_dump -Fc`), retention 7 daily + 4 weekly + 6 monthly, uploaded to S3-compatible storage in a **different provider/region** (e.g. Hetzner Storage Box / Backblaze B2 EU), encrypted at rest.
- Additionally WAL archiving / PITR (e.g. via `pgBackRest` sidecar) once paying customers exist (target RPO 15 min, RTO 2 h).
- **Restore drill before public beta (M4)**, then quarterly: restore latest backup into staging, run smoke e2e suite; documented in `docs/deployment/runbook.md` (minimal version in M4, full runbook M8).
- GDPR: deleted accounts disappear from backups by rotation (≤ 6 months); documented in privacy policy.

## 9. Operations

- Logs: JSON to stdout (Coolify log viewer); optional OTLP to Grafana/Loki/Tempo or similar.
- Uptime monitoring: external monitor on `/health/ready` and a synthetic feed request.
- Scaling: increase api replicas in compose (`deploy.replicas`) — stateless; worker replicas safe via `SKIP LOCKED`; scheduled cron jobs use per-job `dedupe_key` to avoid double enqueue.
- Rollback: redeploy previous image tag in Coolify (safe due to expand/contract migrations). Note: Coolify compose deploys recreate containers, so expect a few seconds of downtime per deploy unless blue/green is configured; expand/contract still matters for rollback.
- Secrets rotation: Stripe/LLM/SMTP keys via Coolify UI + redeploy; feed tokens and API tokens by users.

## 10. Self-hosting outside Coolify

`deploy/docker-compose.yml` + a bundled `postgres` service profile (`--profile with-db`) and `deploy/.env.example` (copy to `deploy/.env`) let anyone run `docker compose -f deploy/docker-compose.yml --profile with-db up -d` behind any reverse proxy (forward to `127.0.0.1:8080`). Billing defaults to `none`, LLM to `none`.
