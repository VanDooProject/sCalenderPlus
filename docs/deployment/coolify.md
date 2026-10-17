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
| **Landing** (separate resource) | `scalenderplus-landing` (Caddy, static; poly: `scalenderplus-poly-landing`) | `www.example.com`, apex redirect | Deployed independently (marketing changes don't redeploy the app), [`deploy/coolify/landing.yml`](../../deploy/coolify/landing.yml), §13. |

Environments: **staging** (deployed by GitHub Actions after every green push to `main`, image tag `main-<sha>`, see §11) and **production** (deploy on release tag `vX.Y.Z`, set up in M4), each a separate Coolify environment with its own database.

Same-origin via `web` keeps cookies simple and hides the api container from Traefik. Alternative (Traefik path routing `app.example.com/api` → api directly) is possible, but Caddy in `web` makes the setup portable outside Coolify.

## 2. Images (`deploy/docker/`)

| Dockerfile | Build | Runtime |
|---|---|---|
| `api.Dockerfile` | `mcr.microsoft.com/dotnet/sdk:10.0` → locked `dotnet restore`, `dotnet publish -c Release` (framework-dependent, RID-neutral, no apphost, OpenAPI export off) | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, non-root `app` user (UID 1654), `ASPNETCORE_HTTP_PORTS=8080` |
| `worker.Dockerfile` | same SDK | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (needs Kestrel only for health endpoint), `ASPNETCORE_HTTP_PORTS=8081` |
| `web.Dockerfile` | `node:22-alpine` + pnpm (corepack, version from `frontend/package.json`) → `vite build` (app) | `caddy:2-alpine` with `deploy/caddy/web.Caddyfile`, non-root UID 10001 on port 8080 (the binary's `cap_net_bind_service` is removed so `no-new-privileges` works); build arg `EDITION=standard\|poly` selects the edition and the image name (`scalenderplus-web`/`scalenderplus-poly-web`: OCI title, `SCAL_WEB_IMAGE`; an unknown edition fails the build), entry point `web-start.sh` |
| `landing.Dockerfile` | `node:22-alpine` + pnpm → `vite-ssg build` (landing), dist check and edition leak check; build args `EDITION` and the site settings of §13 | `caddy:2-alpine` with `deploy/caddy/landing.Caddyfile` (static files, strict CSP, prerendered `404.html`), non-root UID 10001 on port 8080 (`LANDING_PORT`), `HEALTHCHECK` on `/healthz`; image `scalenderplus-landing`/`scalenderplus-poly-landing` by `EDITION` (OCI title) |

All images: multi-stage, non-root, `HEALTHCHECK` defined, OCI labels (version, revision via build args `VERSION`/`REVISION`), pushed to GHCR. Build context is the repository root (`.dockerignore` keeps it small). The build stages run on `$BUILDPLATFORM` and produce architecture-neutral output, so `linux/amd64` and `linux/arm64` images build without emulation; CI (`ci.yml` job `docker`) builds `linux/amd64` only, loads the images, checks each image's OCI title against its name (and that a web image refuses a mismatched `WEB_IMAGE`), scans them with Trivy (fails on CRITICAL vulnerabilities that have a fix; HIGH/CRITICAL report in the job summary) and hands them to `e2e-fullstack`; multi-arch builds and pushes happen in the release/staging workflows. The SPA reads runtime config from `/config.json`, rendered by Caddy's `templates` directive from environment variables on each request (`deploy/caddy/config.json.tmpl`, whitelisted keys only, JSON-escaped) so one image works for staging and prod.

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

Images are `${IMAGE_PREFIX:-ghcr.io/vandooproject}/scalenderplus-{api,worker}:${IMAGE_TAG:-local}` and `${WEB_IMAGE:-scalenderplus-web}` for web. The web edition is a **pair** of variables, because compose can't derive one from the other: `WEB_IMAGE=scalenderplus-web` + `EDITION=standard` (the defaults) or `WEB_IMAGE=scalenderplus-poly-web` + `EDITION=poly` (`EDITION` is the build arg of local builds). The web image knows its own name (set from `EDITION` at build time, also its OCI title) and exits at startup with an error naming the pair when it isn't the `WEB_IMAGE` the compose file asks for (`EXPECTED_WEB_IMAGE`, [`web-start.sh`](../../deploy/docker/web-start.sh)), so a mismatched local build fails fast instead of serving the wrong edition. The services also have `build:` sections: `up` pulls the tag and builds from the checkout only when it cannot be pulled (e.g. the default `local`). All app containers run `read_only` with a `tmpfs` `/tmp`, `cap_drop: [ALL]` and `no-new-privileges`. Environment shared by `migrate`, `api` and `worker`: `ConnectionStrings__Default` from `DATABASE_URL` (default: the bundled `postgres`), `App__PublicBaseUrl` from `APP_URL`, JSON console logs; `api` sets `Database__AutoMigrate=false`.

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
| `RateLimiting__Feed__TokenLimit`, `__TokensPerPeriod`, `__ReplenishmentPeriod` | api | | `10`, `1`, `00:01:00` — per iCal feed token (token bucket: 60/h, bursts of 10) |
| `RateLimiting__FeedPerClient__PermitLimit`, `RateLimiting__FeedPerClient__Window` | api | | `600`, `01:00:00` — per client IP over all iCal feed requests (unknown tokens too) |
| `Feeds__CacheSizeMegabytes`, `Feeds__CacheDuration`, `Feeds__MaxEvents`, `Feeds__MaxTokensPerUser`, `Feeds__MaxShareLinksPerCalendar` | api | | `64`, `00:10:00`, `5000`, `50`, `20` — in-process cache of rendered feeds (per replica), VEVENT cap per feed, active feed links per user and share links per calendar ([ical-caldav.md §1](../architecture/ical-caldav.md#1-ical-subscription-feeds-mvp)) |
| `RateLimiting__TokenPlans__Free`, `__Pro`, `__Team` | api | | `60`, `600`, `1200` requests/min per API token (v1, API tokens) |
| `SignUp__BlockDisposableEmailDomains`, `SignUp__BlockedEmailDomains` | api | | `true` (bundled list of disposable-email providers), extra comma-separated domains (subdomains included) |
| `Smtp__Host`, `Smtp__From` | worker | ✓ | `smtp.example.com`, `noreply@example.com`. The api only queues emails (`email.send` jobs) and needs no SMTP settings |
| `Smtp__Port`, `Smtp__Security`, `Smtp__User`, `Smtp__Password`, `Smtp__FromName`, `Smtp__Timeout` | worker | | `587`, `Auto` (implicit TLS on 465, STARTTLS when offered; also `StartTls`, `SslOnConnect`, `None`), auth only when `User` is set, sender name = `Brand__ProductName` when unset, `00:00:30` |
| `Jobs__Concurrency`, `Jobs__PollInterval`, `Jobs__LeaseDuration`, `Jobs__LeaseRenewalInterval`, `Jobs__BaseRetryDelay`, `Jobs__MaxRetryDelay` | worker | | `4`, `00:00:01`, `00:05:00`, `00:00:30`, `00:00:30`, `01:00:00` (job queue tuning; defaults suit production) |
| `Billing__Provider` | api, worker | | `stripe` (SaaS; until billing lands every account is on plan Free) / `none` (self-host, default: plan `selfhost`) |
| `Billing__Stripe__SecretKey`, `__WebhookSecret`, `__Prices__ProMonthly` … | api, worker | if stripe | |
| `Llm__Provider` | worker, api (dry run) | | `anthropic` / `openai_compatible` / `none` (default) |
| `Llm__ApiKey`, `Llm__Model`, `Llm__BaseUrl` | worker, api | if provider | |
| `Import__Enabled`, `Import__BlockedDomains` | worker | | `true`, `` |
| `Plans__{Free,Pro,Team,SelfHost}__{OwnedCalendars,OwnedGroups,MembersPerGroup,EventsWithOverrides,OverridesPerEvent}` | api, worker | | limit overrides (empty = unlimited, negative fails start); defaults are the numbers of plans.md, `SelfHost` unlimited |
| `Plans__TrialDays` | api, worker | | `90` — reverse trial: Pro limits for this many days from account creation; `0` disables; only with `Billing__Provider=stripe` ([plans.md](../product/plans.md)) |
| `Brand__ProductName` | api, worker | | `sCalenderPlus` — product name in email subjects/bodies, the email sender name (unless `Smtp__FromName`), the 2FA issuer and other server-rendered text ([editions.md §3.2](../product/editions.md#32-backend)); compose: `BRAND_PRODUCT_NAME` |
| `Brand__SupportEmail` | api, worker | | support address shown in emails (optional); compose: `BRAND_SUPPORT_EMAIL` |
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
| `PUBLIC_BRAND_NAME`, `PUBLIC_BRAND_LOGO_URL`, `PUBLIC_BRAND_HUE`, `PUBLIC_BRAND_SUPPORT_EMAIL`, `PUBLIC_BRAND_IMPRINT_URL`, `PUBLIC_BRAND_PRIVACY_URL`, `PUBLIC_BRAND_TERMS_URL` | web | | runtime white-label branding → `/config.json` `brand.{name, logoUrl, hue, supportEmail, imprintUrl, privacyUrl, termsUrl}`; all optional, unset = edition default (standard name `sCalenderPlus`, hue `268` = OKLCH hue 0–360) ([editions.md §3.1](../product/editions.md#31-web-configjson)) |

Compose-level variables (`deploy/docker-compose.yml`, see `deploy/.env.example`): `APP_URL` (→ `App__PublicBaseUrl`), `DATABASE_URL` (→ `ConnectionStrings__Default`), `POSTGRES_PASSWORD` (bundled database), `IMAGE_TAG` and `IMAGE_PREFIX` (image tags), `WEB_IMAGE` (default `scalenderplus-web`, poly: `scalenderplus-poly-web`) and `EDITION` (build arg of local web builds, default `standard`; set both as a pair, see §3), `BRAND_PRODUCT_NAME`/`BRAND_SUPPORT_EMAIL` (→ `Brand__*`), the `PUBLIC_BRAND_*` variables (→ web), `APP_ENVIRONMENT` (→ `PUBLIC_ENVIRONMENT`), `WEB_PORT` (host port), `TRUSTED_PROXY_NETWORKS` (→ `ReverseProxy__KnownNetworks`), `RATE_LIMIT_AUTH_PER_MINUTE`/`RATE_LIMIT_SIGN_UP_PER_HOUR` (→ `RateLimiting__Auth__PermitLimit`/`RateLimiting__SignUp__PermitLimit`, defaults 10 and 5; raised only by the fullstack e2e job), `SMTP_HOST`/`SMTP_PORT`/`SMTP_SECURITY`/`SMTP_USER`/`SMTP_PASSWORD`/`SMTP_FROM` (→ `Smtp__*`; default: the bundled Mailpit of `--profile with-mailpit`, UI on `127.0.0.1:${MAILPIT_PORT:-8025}`). `deploy/coolify/docker-compose.yml` uses `IMAGE_TAG` (default `main`), `IMAGE_PREFIX`, `WEB_IMAGE`, `BRAND_PRODUCT_NAME`, `BRAND_SUPPORT_EMAIL`, the `PUBLIC_BRAND_*` variables, `DATABASE_URL` and `APP_URL` (required), `APP_ENVIRONMENT` (default `staging`), `API_UPSTREAM` (§11 step 6), `SMTP_HOST` and `SMTP_FROM` (required) plus the optional `SMTP_*`, and `TRUSTED_PROXY_NETWORKS` (optional, same default).

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
| `landing` `:8080/healthz` | Caddy static 200. | Container healthcheck. |

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

Written against Coolify **v4.3.23** (single server with Traefik v3.6 and Docker Compose v5; resources from GitHub through a Coolify GitHub App). Pipeline: a push to `main` → `ci` green → [`images.yml`](../../.github/workflows/images.yml) publishes `ghcr.io/vandooproject/scalenderplus-{api,worker,web,poly-web,landing,poly-landing}:main-<sha>` and `:main` (multi-arch) → job `deploy-staging` runs [`.github/scripts/coolify-deploy.sh`](../../.github/scripts/coolify-deploy.sh): pin `IMAGE_TAG=main-<sha>` on the Coolify resource (API, needs a token with `write`), trigger the deploy webhook (`POST /api/v1/deploy?uuid=…`), follow the Coolify deployment until `finished` (needs `read`), then poll `https://<STAGING_URL>/health/ready` until it returns 200. Missing secrets/variables skip the corresponding part with a notice instead of failing.

1. **Images reachable.** After the first `images.yml` run, make the GHCR packages `scalenderplus-api`, `scalenderplus-worker`, `scalenderplus-web` and `scalenderplus-poly-web` public (GitHub → Packages → package settings → *Change visibility*). Private alternative: on the Coolify server run `docker login ghcr.io` with a token that has `read:packages`. GHCR creates every new package **private**, including `scalenderplus-poly-web` the first time it is published: make it public too, or give the poly deployment registry credentials (§12).
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

## 12. A second, independent edition deployment

Each edition ([editions.md](../product/editions.md)) is a separate tenant; nothing is shared with the standard deployment except the Coolify server (optional) and the api/worker images.

1. **Own Coolify project** (e.g. `scalenderplus-poly`) with its own `staging` and `production` environments — never a second resource inside the standard project's environments.
2. **Own database** (§11 step 3) and own backup schedule and S3 bucket/prefix (§11 step 4).
3. **Application** as in §11 step 5 with the same compose file and `WEB_IMAGE=scalenderplus-poly-web`, so the `web` service runs the poly edition's image. The first publish of `scalenderplus-poly-web` creates a **private** GHCR package: make it public (package settings → *Change visibility*) or give the poly Coolify project registry credentials (`docker login ghcr.io` on its server with a `read:packages` token), else its deployment fails to pull `web`. `api` and `worker` use the shared images until edition-specific backend images exist ([editions.md §2.3](../product/editions.md#23-images)).
4. **Own domain** (`APP_URL`), own SMTP sender (`SMTP_FROM`), own Stripe account and price ids (`Billing__Stripe__*`).
5. **Branding**: `BRAND_PRODUCT_NAME`, `BRAND_SUPPORT_EMAIL` (→ `Brand__*`) and the `PUBLIC_BRAND_*` variables (§4) with the poly brand; legal URLs point to the poly operator's imprint, privacy policy and terms.
6. **Plans**: poly Free limits via `Plans__Free__*` ([plans.md §Per-edition defaults](../product/plans.md#per-edition-defaults)); `Plans__TrialDays` as decided.
7. **Own deploy webhook and API token**; the `deploy-staging` job and its secrets are per deployment (a second GitHub environment, e.g. `staging-poly`, once the pipeline deploys both).

## 13. Landing page

The marketing site (`frontend/landing`, [overview.md §4.2](../architecture/overview.md#42-landing-structure-implemented-m7)) is a separate image and Coolify resource, so marketing changes never redeploy the app and the app never depends on it.

**Build-time settings (decision).** The landing is prerendered static HTML whose canonical URLs, `hreflang` links, Open Graph tags and `sitemap.xml` must be absolute URLs, and whose pages show the product name. Rendering them at container start (like the web app's `/config.json`) would mean rewriting every HTML file and the hydrating JavaScript, or a templating proxy in front; both are fragile for SEO-relevant markup. So these are **build args** of `landing.Dockerfile` (→ `VITE_*`, validated by `frontend/landing/src/lib/siteConfig.ts`; an invalid value fails the build), and a white-label deployment builds its own landing image (or forks the content). All are optional:

| Build arg | Default | Used for |
|---|---|---|
| `EDITION` | `standard` | `standard` or `poly` content (`src/editions/<edition>`), image name |
| `SITE_URL` | `https://www.example.com` | origin of the landing: canonical URLs, hreflang, OG images, sitemap, robots.txt, default bot user agent |
| `APP_URL` | `https://app.example.com` | sign-in (`/login`) and sign-up (`/register`) links |
| `BRAND_NAME`, `BRAND_HUE` | the edition's `brand.json` | product name in the pages, palette hue (0–360) |
| `SUPPORT_EMAIL` | none (placeholder shown) | contact on the legal and bot pages, footer |
| `IMPRINT_URL`, `PRIVACY_URL`, `TERMS_URL` | none: the built-in templates `/legal/*` | the operator's own legal pages (footer links there; the template page then links out) |
| `DPA_URL` | none: `/downloads/dpa-template.pdf` (placeholder) | the operator's data processing agreement (download on `/legal/dpa`) |
| `BOT_USER_AGENT` | `sCalenderPlusBot/1.0 (+<SITE_URL>/bot)` | user agent shown on the bot page; keep it equal to the importer's ([llm-import.md §4](../architecture/llm-import.md#4-fetching-safely-and-politely)) |

The published images get `SITE_URL`, `APP_URL` and `SUPPORT_EMAIL` from repository variables (`LANDING_SITE_URL`, `LANDING_APP_URL`, `LANDING_SUPPORT_EMAIL`; poly: `POLY_LANDING_*`) in [`build-images.yml`](../../.github/workflows/build-images.yml); unset, they carry the placeholders above. The legal pages are **templates** with `[placeholders]`, clearly marked for the operator and without invented company data; complete them (fork) or point the `*_URL` args to the operator's pages before going live. The DPA download is a placeholder PDF until `DPA_URL` is set (`pnpm --filter landing render:assets` re-renders it and the OG images).

**Legal links of the app.** The web app's footer links its own template pages unless `PUBLIC_BRAND_IMPRINT_URL`, `PUBLIC_BRAND_PRIVACY_URL` and `PUBLIC_BRAND_TERMS_URL` are set (§4). With a landing, set them to the landing's pages (`https://www.example.com/legal/imprint`, `…/legal/privacy`, `…/legal/terms`), so app and landing link the same documents. The importer bot page (`/bot`) is for operators of websites the importer fetches; its user agent links it, and the app links it from the import source form once the importer UI exists (M6).

**Coolify setup.** In the same project and environment as the stack (or the poly project for `scalenderplus-poly-landing`): *+ New* → *Docker Compose* from the repository with *Docker Compose Location* `/deploy/coolify/landing.yml`, Auto Deploy off; environment `IMAGE_TAG` (`main`, or a release `X.Y.Z`), `IMAGE_PREFIX`, `LANDING_IMAGE` (`scalenderplus-landing` or `scalenderplus-poly-landing`); domain on the `landing` service `https://www.example.com:8080`. Make the GHCR packages `scalenderplus-landing`/`scalenderplus-poly-landing` public (or give the server registry credentials) as in §11 step 1. The apex domain redirects to `www` in Traefik (Coolify *Redirect to www* setting or a redirect label); the container itself redirects `/de/` → `/de` and serves `404.html` for unknown paths. Deploy manually or with the resource's webhook; the staging pipeline does not deploy the landing yet.

**Local / self-hosting.** `docker compose -f deploy/docker-compose.yml --profile with-landing up -d --build landing` builds and serves it on `127.0.0.1:${LANDING_PORT:-8090}` (`LANDING_IMAGE` + `EDITION` as a pair, `LANDING_SITE_URL`, see `deploy/.env.example`). The marketing site is not part of the self-host distribution ([plans.md](../product/plans.md#self-host-stance)); the profile exists for local checks and white-label builds.
