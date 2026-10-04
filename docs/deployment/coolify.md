# Deployment on Coolify

Target: a self-managed [Coolify](https://coolify.io) v4 instance (Docker + Traefik), EU-hosted VPS. The same images and compose file also serve self-hosters outside Coolify.

## 1. Topology

| Coolify resource | Image | Domain | Notes |
|---|---|---|---|
| **PostgreSQL 17** | Coolify-managed database (`postgres:17-alpine`) | internal only | Coolify scheduled backups to S3. |
| **sCalenderPlus stack** (Docker Compose resource) | [`deploy/coolify/docker-compose.yml`](../../deploy/coolify/docker-compose.yml) | | Services below; published images only (§11). |
| └ `migrate` | `scalenderplus-api` with `migrate` command | – | One-shot, must succeed before api/worker start. |
| └ `api` | `scalenderplus-api` | internal (proxied by `web`) | Port 8080. |
| └ `worker` | `scalenderplus-worker` | – | No public port; health on 8081 internal. |
| └ `web` | `scalenderplus-web` (Caddy + SPA) | `app.example.com` | Port 8080 (non-root). Proxies `/api`, `/ical`, `/dav`, `/.well-known`, `/health`, `/openapi` to `api:8080`. |
| **Landing** (separate resource) | `scalenderplus-landing` (Caddy, static) | `www.example.com`, apex redirect | Deployed independently (marketing changes don't redeploy the app). |

Environments: **staging** (deployed by GitHub Actions after every green push to `main`, image tag `main-<sha>`, see §11) and **production** (deploy on release tag `vX.Y.Z`, set up in M4), each a separate Coolify environment with its own database.

Same-origin via `web` keeps cookies simple and hides the api container from Traefik. Alternative (Traefik path routing `app.example.com/api` → api directly) is possible, but Caddy in `web` makes the setup portable outside Coolify.

## 2. Images (`deploy/docker/`)

| Dockerfile | Build | Runtime |
|---|---|---|
| `api.Dockerfile` | `mcr.microsoft.com/dotnet/sdk:10.0` → locked `dotnet restore`, `dotnet publish -c Release` (framework-dependent, RID-neutral, no apphost, OpenAPI export off) | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, non-root `app` user (UID 1654), `ASPNETCORE_HTTP_PORTS=8080` |
| `worker.Dockerfile` | same SDK | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (needs Kestrel only for health endpoint), `ASPNETCORE_HTTP_PORTS=8081` |
| `web.Dockerfile` | `node:22-alpine` + pnpm (corepack, version from `frontend/package.json`) → `vite build` (app) | `caddy:2-alpine` with `deploy/caddy/web.Caddyfile`, non-root UID 10001 on port 8080 (the binary's `cap_net_bind_service` is removed so `no-new-privileges` works) |
| `landing.Dockerfile` | `node:22-alpine` → `vite-ssg build` | `caddy:2-alpine` with `landing.Caddyfile` |

All images: multi-stage, non-root, `HEALTHCHECK` defined, OCI labels (version, revision via build args `VERSION`/`REVISION`), pushed to GHCR. Build context is the repository root (`.dockerignore` keeps it small). The build stages run on `$BUILDPLATFORM` and produce architecture-neutral output, so `linux/amd64` and `linux/arm64` images build without emulation; CI (`ci.yml` job `docker`) builds `linux/amd64` only, loads the images, scans them with Trivy (fails on CRITICAL vulnerabilities that have a fix; HIGH/CRITICAL report in the job summary) and hands them to `e2e-fullstack`; multi-arch builds and pushes happen in the release/staging workflows. The SPA reads runtime config from `/config.json`, rendered by Caddy's `templates` directive from environment variables on each request (`deploy/caddy/config.json.tmpl`, whitelisted keys only, JSON-escaped) so one image works for staging and prod.

Chiseled images have no shell/curl, so container health checks for api/worker use a tiny built-in command: `dotnet SCalenderPlus.Api.dll healthcheck` (HTTP GET to localhost, exit 0/1). It probes `/health/ready` by default (`healthcheck live` probes `/health/live`); the target is the first `http://` entry of `ASPNETCORE_URLS` (which Kestrel prefers; wildcard hosts become `127.0.0.1`), else the port from `ASPNETCORE_HTTP_PORTS`, falling back to 8080 (api) / 8081 (worker), and `--url <base-url>` overrides all of them. Both hosts listen on those default ports when neither `ASPNETCORE_URLS` nor `ASPNETCORE_HTTP_PORTS` is set; since the aspnet base image sets `ASPNETCORE_HTTP_PORTS=8080`, `worker.Dockerfile` must set it to `8081`.

## 3. docker-compose.yml

[`deploy/docker-compose.yml`](../../deploy/docker-compose.yml) (variables in [`deploy/.env.example`](../../deploy/.env.example)):

| Service | Image / command | Notes |
|---|---|---|
| `migrate` | api image, `command: ["migrate"]` | One-shot, `restart: "no"`; waits for `postgres` healthy when the bundled database runs (`depends_on … required: false`). |
| `api` | api image | `depends_on: migrate: service_completed_successfully`; healthcheck `dotnet SCalenderPlus.Api.dll healthcheck` (`/health/ready`). |
| `worker` | worker image | Same `depends_on`; healthcheck `dotnet SCalenderPlus.Worker.dll healthcheck` on 8081. |
| `web` | web image | `depends_on: api: service_healthy`; healthcheck `wget http://127.0.0.1:8080/healthz`; published on `127.0.0.1:${WEB_PORT:-8080}` only (Traefik reaches the container port directly). |
| `postgres` | `postgres:17-alpine`, volume `postgres-data` | Only with `--profile with-db` (self-hosting, local, CI). Coolify uses a Coolify-managed database instead. |

Images are `${IMAGE_PREFIX:-ghcr.io/vandooproject}/scalenderplus-{api,worker,web}:${IMAGE_TAG:-local}`. The services also have `build:` sections: `up` pulls the tag and builds from the checkout only when it cannot be pulled (e.g. the default `local`). All app containers run `read_only` with a `tmpfs` `/tmp`, `cap_drop: [ALL]` and `no-new-privileges`. Environment shared by `migrate`, `api` and `worker`: `ConnectionStrings__Default` from `DATABASE_URL` (default: the bundled `postgres`), `App__PublicBaseUrl` from `APP_URL`, JSON console logs; `api` sets `Database__AutoMigrate=false`.

```sh
# Self-hosting / local / CI: bundled PostgreSQL
docker compose -f deploy/docker-compose.yml --profile with-db up -d --build
curl http://localhost:8080/health/ready        # through web → api
docker compose -f deploy/docker-compose.yml --profile with-db down -v
```

Coolify does **not** use this file but [`deploy/coolify/docker-compose.yml`](../../deploy/coolify/docker-compose.yml): same services, healthchecks and hardening, but published images only (no `build:` sections, which Coolify would build on the server), no bundled `postgres` (a Coolify-managed database instead), no host port (the Coolify proxy itself binds host port 8080; Traefik routes the domain to the `web` container), `migrate` marked `exclude_from_hc: true` (a Coolify-only key that plain `docker compose` rejects), and required variables (`${DATABASE_URL:?}`, `${APP_URL:?}`, `${SMTP_HOST:?}`, `${SMTP_FROM:?}`) that Coolify shows as required in its UI. Step-by-step setup in §11.

**Token-bearing URLs**: Traefik access logs (if enabled on the Coolify server) and Caddy logs in `web` must not record `/ical/` paths or the `token` query parameter of the email links (`/verify-email`, `/reset-password`, `/invite`) in clear — disable access logs (Traefik) or mask them (Caddy). `web.Caddyfile` also replaces the value of every query parameter whose name contains `token` with `[REDACTED]`, in the URI and in the `Referer` (the SPA's API calls from such a page send it). `web.Caddyfile` rewrites `request>uri` (query string included) and `Referer` to `…/ical/[REDACTED]` in both the access log and the default logger, which also receives the error logs (e.g. a 502 from `reverse_proxy` logs the full request); the match is unanchored and tolerates percent-encoding because the path matcher also proxies `//ical/…` and `/%69cal/…`. Caddy omits `Cookie`/`Authorization` values by default. Verified by the M4 log redaction test.

## 4. Environment variables

| Variable | Service | Required | Example / default |
|---|---|---|---|
| `ConnectionStrings__Default` | api, worker, migrate | ✓ | `Host=pg;Database=scal;Username=scal;Password=…;Maximum Pool Size=50` |
| `App__PublicBaseUrl` | api, worker | ✓ | `https://app.example.com` (feed links, emails) |
| `App__LandingUrl` | api | | `https://www.example.com` |
| `ReverseProxy__KnownNetworks` | api | | comma-separated CIDRs whose `X-Forwarded-For`/`-Proto` are trusted; compose default (`TRUSTED_PROXY_NETWORKS`): `10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,fc00::/7` (web and Traefik on the private Docker networks; the api has no public port). Loopback is always trusted; invalid entries fail startup |
| `ReverseProxy__KnownProxies` | api | | comma-separated IPs of individual trusted proxies (optional) |
| – (Data Protection keys) | api, worker | | no setting: the key ring is stored in the `data_protection_keys` table (`PersistKeysToDbContext`, application name `scalenderplus` shared by api and worker) — no volume needed. Keys are stored unencrypted (DB access = key access; the startup log warns once per new key); a key-encryption certificate is a possible later hardening |
| `Auth__External__Google__ClientId/Secret` (Microsoft, Apple) | api | | v1 |
| `RateLimiting__Auth__PermitLimit`, `RateLimiting__Auth__Window` | api | | `10`, `00:01:00` — per client IP: login, 2FA, email confirmation, password reset |
| `RateLimiting__SignUp__PermitLimit`, `RateLimiting__SignUp__Window` | api | | `5`, `01:00:00` — per client IP: sign-ups |
| `RateLimiting__InviteCreate__*`, `RateLimiting__InviteAccept__*` | api | | `50`, `01:00:00` and `10`, `00:01:00` — per signed-in user: invites created, invite acceptances |
| `RateLimiting__InvitePreview__PermitLimit`, `RateLimiting__InvitePreview__Window` | api | | `30`, `00:01:00` — per client IP: invite previews (invite page before sign-in) |
| `RateLimiting__Session__PermitLimit`, `RateLimiting__Session__Window` | api | | `600`, `00:01:00` — per signed-in user (web app sessions; abuse limit only) |
| `RateLimiting__TokenPlans__Free`, `__Pro`, `__Team` | api | | `60`, `600`, `1200` requests/min per API token (v1, API tokens) |
| `SignUp__BlockDisposableEmailDomains`, `SignUp__BlockedEmailDomains` | api | | `true` (bundled list of disposable-email providers), extra comma-separated domains (subdomains included) |
| `Smtp__Host`, `Smtp__From` | worker | ✓ | `smtp.example.com`, `noreply@example.com`. The api only queues emails (`email.send` jobs) and needs no SMTP settings |
| `Smtp__Port`, `Smtp__Security`, `Smtp__User`, `Smtp__Password`, `Smtp__FromName`, `Smtp__Timeout` | worker | | `587`, `Auto` (implicit TLS on 465, STARTTLS when offered; also `StartTls`, `SslOnConnect`, `None`), auth only when `User` is set, `sCalenderPlus`, `00:00:30` |
| `Jobs__Concurrency`, `Jobs__PollInterval`, `Jobs__LeaseDuration`, `Jobs__LeaseRenewalInterval`, `Jobs__BaseRetryDelay`, `Jobs__MaxRetryDelay` | worker | | `4`, `00:00:01`, `00:05:00`, `00:00:30`, `00:00:30`, `01:00:00` (job queue tuning; defaults suit production) |
| `Billing__Provider` | api, worker | | `stripe` (SaaS; until billing lands every account is on plan Free) / `none` (self-host, default: plan `selfhost`) |
| `Billing__Stripe__SecretKey`, `__WebhookSecret`, `__Prices__ProMonthly` … | api, worker | if stripe | |
| `Llm__Provider` | worker, api (dry run) | | `anthropic` / `openai_compatible` / `none` (default) |
| `Llm__ApiKey`, `Llm__Model`, `Llm__BaseUrl` | worker, api | if provider | |
| `Import__Enabled`, `Import__BlockedDomains` | worker | | `true`, `` |
| `Plans__{Free,Pro,Team,SelfHost}__{OwnedCalendars,OwnedGroups,MembersPerGroup,EventsWithOverrides,OverridesPerEvent}` | api, worker | | limit overrides (empty = unlimited, negative fails start); defaults are the numbers of plans.md, `SelfHost` unlimited |
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

Compose-level variables (`deploy/docker-compose.yml`, see `deploy/.env.example`): `APP_URL` (→ `App__PublicBaseUrl`), `DATABASE_URL` (→ `ConnectionStrings__Default`), `POSTGRES_PASSWORD` (bundled database), `IMAGE_TAG` and `IMAGE_PREFIX` (image tags), `APP_ENVIRONMENT` (→ `PUBLIC_ENVIRONMENT`), `WEB_PORT` (host port), `TRUSTED_PROXY_NETWORKS` (→ `ReverseProxy__KnownNetworks`), `RATE_LIMIT_AUTH_PER_MINUTE`/`RATE_LIMIT_SIGN_UP_PER_HOUR` (→ `RateLimiting__Auth__PermitLimit`/`RateLimiting__SignUp__PermitLimit`, defaults 10 and 5; raised only by the fullstack e2e job), `SMTP_HOST`/`SMTP_PORT`/`SMTP_SECURITY`/`SMTP_USER`/`SMTP_PASSWORD`/`SMTP_FROM` (→ `Smtp__*`; default: the bundled Mailpit of `--profile with-mailpit`, UI on `127.0.0.1:${MAILPIT_PORT:-8025}`). `deploy/coolify/docker-compose.yml` uses `IMAGE_TAG` (default `main`), `IMAGE_PREFIX`, `DATABASE_URL` and `APP_URL` (required), `APP_ENVIRONMENT` (default `staging`), `API_UPSTREAM` (§11 step 6), `SMTP_HOST` and `SMTP_FROM` (required) plus the optional `SMTP_*`, and `TRUSTED_PROXY_NETWORKS` (optional, same default).

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

## 11. Staging on Coolify (step by step)

Written against Coolify **v4.3.23** (single server with Traefik v3.6 and Docker Compose v5; resources from GitHub through a Coolify GitHub App). Pipeline: a push to `main` → `ci` green → [`images.yml`](../../.github/workflows/images.yml) publishes `ghcr.io/vandooproject/scalenderplus-{api,worker,web}:main-<sha>` and `:main` (multi-arch) → job `deploy-staging` runs [`.github/scripts/coolify-deploy.sh`](../../.github/scripts/coolify-deploy.sh): pin `IMAGE_TAG=main-<sha>` on the Coolify resource (API, needs a token with `write`), trigger the deploy webhook (`POST /api/v1/deploy?uuid=…`), follow the Coolify deployment until `finished` (needs `read`), then poll `https://<STAGING_URL>/health/ready` until it returns 200. Missing secrets/variables skip the corresponding part with a notice instead of failing.

1. **Images reachable.** After the first `images.yml` run, make the GHCR packages `scalenderplus-api`, `scalenderplus-worker` and `scalenderplus-web` public (GitHub → Packages → package settings → *Change visibility*). Private alternative: on the Coolify server run `docker login ghcr.io` with a token that has `read:packages`.
2. **Project and environment.** Coolify → *Projects* → *+ Add* `scalenderplus`; it gets the environment `production`; add an environment `staging` (production is configured the same way in M4).
3. **Database.** In `staging`: *+ New* → *Databases* → **PostgreSQL** (image `postgres:17-alpine`, same major as CI and the bundled database). Set user `scal`, database `scal` and a generated password; leave *Make it publicly available* off; *Start*. The *Postgres URL (internal)* has the form `postgres://scal:<password>@<database-uuid>:5432/scal`. Npgsql does not accept URLs, so translate it into a connection string for step 6: `Host=<database-uuid>;Port=5432;Database=scal;Username=scal;Password=<password>;Maximum Pool Size=50`.
4. **Backups.** Coolify → *S3 Storages* → add an S3-compatible bucket at a different provider/region (see §8), *Validate connection*. Database → *Backups* → *+ Add* scheduled backup: frequency `0 */6 * * *` for production (staging: daily, e.g. `0 3 * * *`), enable *Save to S3*, set retention (production: §8; staging: 7 days). Run *Backup now* once and test a restore (§8 restore drill).
5. **Application.** In `staging`: *+ New* → *Private Repository (with GitHub App)* (the existing Coolify GitHub App with access to `VanDooProject/sCalenderPlus`; *Public Repository* also works for the public repo) → repository `VanDooProject/sCalenderPlus`, branch `main`, **Build Pack: Docker Compose**, *Base Directory* `/`, *Docker Compose Location* `/deploy/coolify/docker-compose.yml` → *Continue*. Coolify reads the compose file from git but only pulls images (there are no `build:` sections). Then in the resource:
    - *Configuration → Advanced*: turn **Auto Deploy off** (GitHub Actions triggers the deployment once the images exist; a Coolify deploy on push would run before they are published) and turn **Connect To Predefined Network on** (the stack must reach the Coolify-managed database on the `coolify` network).
    - *Domains for web*: `https://staging.example.com:8080` (the `:8080` is the container port Traefik routes to, not a public port). Leave the other services without a domain. Point the DNS record (or Cloudflare tunnel route) for the host at the server; Traefik obtains the Let's Encrypt certificate.
6. **Environment variables** (*Environment Variables*; mark `DATABASE_URL` as locked/secret):

    | Variable | Staging value |
    |---|---|
    | `IMAGE_TAG` | `main` (overwritten with `main-<sha>` by `deploy-staging` when the token has `write`) |
    | `IMAGE_PREFIX` | `ghcr.io/vandooproject` |
    | `DATABASE_URL` | connection string from step 3 |
    | `APP_URL` | `https://staging.example.com` |
    | `APP_ENVIRONMENT` | `staging` (the SPA shows a non-production badge) |
    | `API_UPSTREAM` | `api-<resource-uuid>:8080` |
    | `SMTP_HOST`, `SMTP_FROM` | SMTP relay of the mail provider and the sender address (required by the worker); `SMTP_PORT` (default `587`), `SMTP_SECURITY` (default `Auto`), `SMTP_USER`, `SMTP_PASSWORD` (secret) as needed. Staging may use a Mailpit service instead of real delivery. |

    `API_UPSTREAM`: with *Connect To Predefined Network* every container also joins the shared `coolify` network, where the bare name `api` can resolve to another stack's `api` (e.g. production on the same server). Coolify names containers `<service>-<resource-uuid>`; the resource uuid is in the resource URL and in the deploy webhook (`uuid=…`).
7. **`migrate` and health status.** `migrate` runs once per deployment (`restart: 'no'`) before `api` and `worker` (`depends_on: service_completed_successfully`); a failed migration fails the deployment and the old containers keep running. It is marked `exclude_from_hc: true` (Coolify also ignores `restart: 'no'` services for the resource status), so its `exited (0)` state doesn't turn the resource *degraded*. The other services report through their image healthchecks.
8. **First deployment.** *Deploy* in Coolify, watch the deployment log (pull, `migrate` exits 0, `api`/`worker`/`web` healthy), then `curl https://staging.example.com/health/ready` → 200 `{"status":"Healthy",…}`.
9. **Deploy webhook and API token.** Resource → *Webhooks* → copy the **Deploy Webhook** (`https://<coolify>/api/v1/deploy?uuid=<resource-uuid>&force=false`). Coolify → *Settings → Advanced*: enable **API Access** (and restrict *Allowed IPs* only if GitHub-hosted runners can still reach it). *Keys & Tokens → API tokens* → create a token for the root team with **`deploy`** and **`read`** (follow the deployment) and **`write`** (pin `IMAGE_TAG`; without it staging runs the moving `main` tag, which Coolify pulls on every deployment). The Coolify URL must be reachable from GitHub-hosted runners (not only through an internal network or an access proxy).
10. **GitHub configuration** (repository *Settings → Secrets and variables → Actions*, or the `staging` environment that `deploy-staging` uses): secrets `COOLIFY_WEBHOOK_URL` (step 9) and `COOLIFY_TOKEN`; variable `STAGING_URL` = `staging.example.com` (host name; a scheme or path is stripped). Optionally add protection rules to the `staging` environment.
11. **Verify the pipeline.** Merge something to `main`: `ci` → `images` (`publish main images`, `deploy staging`). The job summary shows the pushed tags, the Coolify deployment status and the health check result. Re-run with *Actions → images → Run workflow* on `main`.

Rollback: set `IMAGE_TAG` to an earlier `main-<sha>` in Coolify and *Redeploy* (safe because migrations are expand/contract, §7); the next push to `main` pins the new tag again. Manual deployment without GitHub: `COOLIFY_WEBHOOK_URL=… COOLIFY_TOKEN=… IMAGE_TAG=main-<sha> HEALTH_URL=https://staging.example.com/health/ready .github/scripts/coolify-deploy.sh`.
