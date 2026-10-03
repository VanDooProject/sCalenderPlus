# Architecture Overview

## 1. System context

```text
          Browsers (PWA)                Native calendar apps            Integrators / future native apps
               │                       (Apple, Google, Outlook)                     │
               │ HTTPS (cookie)          │ HTTPS (iCal token / CalDAV)              │ HTTPS (Bearer token)
               ▼                         ▼                                          ▼
   ┌──────────────────────┐   ┌───────────────────────────────────────────────────────────┐
   │  web (Caddy)         │──▶│  api  (ASP.NET Core, .NET 10)                              │
   │  SPA static files    │   │  /api/v1/*  /ical/v1/*  /dav/* (later)  /health/*          │
   │  reverse proxy /api  │   └───────────────┬───────────────────────────────────────────┘
   └──────────────────────┘                   │ EF Core / Npgsql
   ┌──────────────────────┐                   ▼
   │ landing (Caddy)      │         ┌───────────────────┐        ┌──────────────────────────┐
   │ static marketing SSG │         │  PostgreSQL 17    │◀──────▶│ worker (.NET 10)         │
   └──────────────────────┘         │  data + job queue │        │ jobs: LLM import, ICS    │
                                    └───────────────────┘        │ sync, reminders, emails, │
                                                                 │ webhooks, retention      │
                                                                 └─────────┬────────────────┘
                                                                           ▼
                                                     SMTP · Stripe · LLM provider · event websites
```

| Component | Responsibility | Scaling |
|---|---|---|
| **api** | REST API, auth, iCal feeds, CalDAV (later), Stripe webhooks, OpenAPI doc. Stateless. | Horizontal (N replicas). |
| **worker** | Background jobs from the Postgres job queue and cron schedules. No inbound HTTP except health. | 1–N replicas (SKIP LOCKED makes it safe). |
| **web** | Serves the Vue SPA; reverse-proxies `/api`, `/ical`, `/dav`, `/.well-known` to api so the app is **same-origin** (simple cookies, no CORS). | Static. |
| **landing** | Static marketing site (prerendered Vue). Separate domain (`www.`). | Static. |
| **postgres** | System of record, job queue, full-text search. | Vertical; managed backups. |

**Why a separate worker:** LLM calls take seconds to minutes and fetch untrusted websites. Isolating them protects API latency, lets us restrict the worker's network egress differently, and scale independently. Both share the same Application/Infrastructure code.

**Why no Redis/message broker:** Postgres covers queueing (`FOR UPDATE SKIP LOCKED`), caching needs are small (feeds use ETags), and Coolify ops stay simple. Revisit if job throughput > ~50 jobs/s.

## 2. Repository layout (monorepo)

```text
/
├── backend/
│   ├── SCalenderPlus.slnx             # XML solution format (.NET 10 default)
│   ├── Directory.Build.props          # nullable, warnings-as-errors, analyzers, LangVersion, lock files
│   ├── Directory.Packages.props       # central package versions
│   ├── src/
│   │   ├── SCalenderPlus.Core/         # domain model + permission engine + recurrence + entitlement rules (no EF, no ASP.NET)
│   │   ├── SCalenderPlus.Application/  # use cases (commands/queries), validation, ports (interfaces)
│   │   ├── SCalenderPlus.Infrastructure/ # EF Core DbContext + migrations, Ical.Net, LLM, email, Stripe, fetcher
│   │   ├── SCalenderPlus.Api/          # ASP.NET Core host: endpoints, auth, OpenAPI, iCal/CalDAV handlers
│   │   └── SCalenderPlus.Worker/       # Generic host: job runner, schedulers
│   └── tests/
│       ├── SCalenderPlus.Core.Tests/           # pure unit + property tests
│       ├── SCalenderPlus.Application.Tests/    # use cases with fakes
│       ├── SCalenderPlus.IntegrationTests/     # API + Postgres (Testcontainers), iCal golden files
│       └── SCalenderPlus.ArchitectureTests/    # NetArchTest layering rules (references every src project)
├── frontend/
│   ├── package.json                   # pnpm workspace root
│   ├── packages/
│   │   ├── api-client/                # generated OpenAPI types + typed fetch client + MSW mocks
│   │   └── ui/                        # shared design tokens & components (used by app + landing)
│   ├── app/                           # Vue 3 SPA / PWA
│   └── landing/                       # marketing site (vite-ssg)
├── e2e/                               # Playwright: mocked/ and fullstack/ projects
├── deploy/
│   ├── docker/                        # Dockerfiles: api, worker, web, landing
│   ├── docker-compose.yml             # production-like stack (Coolify)
│   ├── docker-compose.dev.yml         # Postgres + Mailpit for local dev
│   └── caddy/                         # Caddyfiles
├── docs/
├── .github/workflows/
├── release-please-config.json, .release-please-manifest.json
└── commitlint.config.mjs
```

### Layering rules (pragmatic clean architecture)

- `Core` depends on nothing but NodaTime. It contains the parts that must be provably correct: permission engine, recurrence expansion helpers, plan-limit rules, dedupe-key computation.
- `Application` depends on `Core`. Use cases are plain classes (`CreateEventHandler`) invoked directly by endpoints — **no MediatR** (indirection without benefit; MediatR is also commercially licensed now). Application **may use `AppDbContext` directly** via an `IAppDbContext` interface — no generic repository layer over EF.
- `Infrastructure` implements ports (`ILlmExtractor`, `IEmailSender`, `IBillingProvider`, `IWebFetcher`, `IClock`).
- `Api` and `Worker` are composition roots only.
- Architecture rules enforced by NetArchTest-based tests in `SCalenderPlus.ArchitectureTests` (a separate project because it must reference every layer, including the hosts). "Composition root only" means: nothing references Api/Worker, Api and Worker do not reference each other, and neither touches EF Core or Npgsql directly (data access goes through Application/Infrastructure).

## 3. Backend key libraries

| Concern | Choice | Why / alternatives |
|---|---|---|
| Web framework | ASP.NET Core **Minimal APIs** with endpoint groups | Less ceremony, native OpenAPI metadata, fast. Controllers would also work; we don't need them. |
| OpenAPI | `Microsoft.AspNetCore.OpenApi` (built-in) + **Scalar** UI in dev | First-party doc generation since .NET 9; Swashbuckle no longer default. |
| Validation | .NET 10 built-in minimal API validation (DataAnnotations) + explicit domain checks | FluentValidation only if rules outgrow attributes. |
| ORM | **EF Core 10 + Npgsql**, `EFCore.NamingConventions` (snake_case) | Requirement; migrations in Infrastructure. Raw SQL (via EF `SqlQuery`) for hot listing queries. |
| Time | **NodaTime** (+ `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`) | `ZonedDateTime`/`LocalDate`/IANA tzdb done right; `DateTime` is a known source of TZ bugs. |
| iCalendar | **Ical.Net** (v5) | Mature RFC 5545 parse/serialize + RRULE evaluation. Wrapped behind `IICalendarSerializer` so we can patch/replace. |
| Auth | **ASP.NET Core Identity** (cookie auth, TOTP 2FA; **passkeys** in .NET 10 from v1), PAT bearer handler (custom) | Proven password hashing, lockout, 2FA. OpenIddict added later for OAuth2/PKCE for native apps. No self-made JWT for the web (cookies are safer for a same-origin SPA). |
| Jobs / scheduling | **Own Postgres job queue** (`jobs` table, `SKIP LOCKED`) + **Cronos** for cron parsing, run by a `BackgroundService` | Transparent, zero extra infra, transactional enqueue with business data (outbox for free). Alternatives: Hangfire (good dashboard, but extra schema and pro features licensed), Quartz.NET (heavyweight clustering config). |
| HTML processing | **AngleSharp** | Sanitize/strip HTML, extract JSON-LD, readable text. |
| LLM | Provider abstraction `ILlmExtractor`; default **Anthropic** via official Anthropic .NET SDK; `OpenAiCompatibleExtractor` for self-host/local models | See [llm-import.md](llm-import.md). |
| Email | **MailKit** via SMTP (`IEmailSender`, worker only); use cases queue mails with `IEmailOutbox` as `email.send` jobs (sent after commit, retried). Templates: a minimal code-based layout (`EmailTemplate`: subject, paragraphs, call-to-action → text + HTML, all values encoded); **Fluid** (Liquid) once copy needs to be editable outside code | Provider-agnostic; Mailpit in dev and tests. |
| Billing | **Stripe.net** behind `IBillingProvider` | See [plans.md](../product/plans.md). |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly v8) | Retries/timeouts for LLM, fetch, webhooks. |
| Rate limiting | Built-in `Microsoft.AspNetCore.RateLimiting` | Per IP / per token / per feed token. |
| Observability | `ILogger` JSON console + **OpenTelemetry** (traces, metrics, OTLP exporter optional) | Coolify collects stdout; OTLP if operator wants. |
| Testing | **xUnit v3** on Microsoft.Testing.Platform (`global.json` `test.runner`), **Testcontainers** (Postgres), **Respawn**, **CsCheck** (property tests; plain C#, no F# runtime, works with any test framework), **Verify** (snapshot/golden iCal files) | |
| Ids | UUIDv7 (`Guid.CreateVersion7()`) | Time-ordered → good B-tree locality; safe to expose. |

## 4. Frontend key libraries

| Concern | Choice | Why / alternatives |
|---|---|---|
| Framework | **Vue 3** (`<script setup>`, TS strict) + **Vite** | Requirement. |
| Package manager | **pnpm** workspaces | Fast, strict; monorepo-friendly. |
| Routing / state | Vue Router; **Pinia** for UI/session state; **TanStack Query (vue-query)** for server state | Server cache, invalidation, optimistic updates without hand-written stores. |
| API client | **openapi-typescript** (types) + **openapi-fetch** | Tiny runtime, types straight from OpenAPI; no codegen of classes. See [api.md](api.md). |
| Calendar grid | **FullCalendar** v6 (MIT standard plugins: daygrid, timegrid, list, interaction, rrule) | Mature, accessible-ish, handles DnD/resizing. Premium (resource) plugins not needed. Alternative: Schedule-X. |
| Styling / components | **Tailwind CSS v4** + **Reka UI** (headless, accessible primitives) | Accessible primitives + design tokens; dark mode via CSS variables. |
| Forms | **VeeValidate** + Zod schemas | |
| i18n | **vue-i18n** (en, de) | |
| Dates | **Temporal polyfill** (`temporal-polyfill`) | Correct time zones in the browser; aligns with NodaTime concepts. |
| PWA | **vite-plugin-pwa** (Workbox) | Install, offline shell, web push. |
| Mocks | **MSW** handlers in `packages/api-client` | Shared by dev "mock mode", Vitest and Playwright mocked e2e. |
| Unit tests | **Vitest** + Vue Test Utils | |
| Landing | **vite-ssg** + shared `ui` package | Prerendered static HTML for SEO; same stack as app. |

## 5. Cross-cutting decisions

- **Same-origin deployment**: `app.example.com` serves SPA and proxies the API → session cookie `__Host-scal`, `SameSite=Lax`, `HttpOnly`, `Secure`; CSRF protection via required header `X-Requested-With: scal` on unsafe methods plus antiforgery token for form posts. Native clients use bearer tokens (no cookies).
- **Configuration** via environment variables only (`Section__Key`), validated at startup (`ValidateOnStart`). See [coolify.md](../deployment/coolify.md).
- **Multi-tenancy**: single database, row-level ownership via the permission engine; no Postgres RLS in MVP (all access goes through the engine; RLS considered as defense-in-depth later).
- **Time**: all instants stored as `timestamptz` (UTC); wall-clock + IANA zone kept for events; server never uses the host time zone. `IClock` (NodaTime) injected for testability.
- **Feature flags**: simple DB table + config overrides; used for gradual rollout (CalDAV, imports).
- **Security headers**: CSP (strict, no inline scripts), HSTS, Referrer-Policy `same-origin` (feed tokens must never leak via Referer).
- **Audit**: domain operations call `IAuditLog.Record(action, resourceType, resourceId, before, after)`, which stages an `audit_events` row in the same unit of work as the mutation (same transaction); see data-model.md §8.
- **Outbox**: domain changes enqueue jobs (webhooks, notifications) in the same transaction via the `jobs` table.

## 6. Job types (worker)

Queue mechanics (`Application/Jobs`, `Infrastructure/Jobs`, data-model.md §8): use cases stage jobs with `IJobScheduler.Enqueue` in the same unit of work as their data (`IAppDbContext.SaveChangesAsync` commits both — transactional outbox); `EnqueueUniqueAsync` deduplicates by key for schedules. The worker runs `Jobs__Concurrency` slots that claim one due job each (`FOR UPDATE SKIP LOCKED` + lease), run its `IJobHandler` in a fresh DI scope while renewing the lease, then delete it, retry it with exponential backoff, or dead-letter it (`PermanentJobFailureException` or attempts exhausted). Delivery is at-least-once (a crashed worker's job runs again when its lease expires), so handlers must be idempotent. Each claim attempt and lease renewal beats the `job-loop` readiness heartbeat; an idle worker polls every `Jobs__PollInterval`.

| Job | Trigger | Notes |
|---|---|---|
| `import.run` | cron per import source | See llm-import.md. |
| `ics.sync` | cron per ICS subscription | Conditional GET. |
| `reminder.dispatch` | every minute: due reminders | Email + web push. |
| `email.send` | enqueued (`IEmailOutbox`) | Retries with backoff (8 attempts); invalid/rejected recipient → dead letter. |
| `webhook.deliver` | enqueued on change | HMAC-signed, exponential backoff up to 24 h, auto-disable after 50 consecutive failures. |
| `digest.build` | daily 06:00 per user TZ bucket | |
| `retention.trim` | daily | Audit/history per plan retention; expired tokens/invites. |
| `tzdb.recompute` | on deploy when tzdb version changes | Recompute UTC instants of future events. |
| `billing.reconcile` | daily | Compare with Stripe; repair missed webhooks. |
