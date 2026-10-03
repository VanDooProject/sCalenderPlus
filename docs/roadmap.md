# Roadmap

Milestones are ordered; each ends in a deployable state on staging. Each checklist item is sized to become **one GitHub issue / one PR** (title = conventional commit style PR title). Format: **Title** — description. *AC:* acceptance criteria.

MVP (public beta) = M0–M4 + M3 essentials. Paid launch (v1) = M5–M7. Later = M8+.

---

## M0 — Scaffolding and CI

- [ ] **chore: initialize monorepo layout** — Create `backend/`, `frontend/`, `e2e/`, `deploy/`, `.editorconfig`, `.gitignore`, root README linking docs. *AC:* layout matches architecture/overview.md §2.
- [ ] **build(api): create .NET 10 solution and projects** — `SCalenderPlus.{Core,Application,Infrastructure,Api,Worker}` + test projects, `Directory.Build.props` (nullable, warnings as errors), central package management. *AC:* `dotnet build` and `dotnet test` pass with one sample test each.
- [ ] **test(core): add architecture rule tests** — NetArchTest rules: Core has no EF/ASP.NET refs; Api/Worker are only composition roots. *AC:* violating reference fails tests.
- [ ] **feat(api): health endpoints and structured logging** — `/health/live`, `/health/ready` (DB check), JSON console logging, OpenTelemetry wiring (exporter optional). *AC:* integration test hits both endpoints.
- [ ] **feat(db): EF Core + Npgsql + NodaTime setup with first migration** — `AppDbContext`, snake_case naming, `migrate` CLI command with advisory lock. *AC:* migration applies to empty Testcontainers Postgres; `migrate` exits 0 twice in a row.
- [ ] **build(web): scaffold Vue 3 + Vite + TS app with pnpm workspace** — `frontend/app`, `packages/api-client`, `packages/ui`; Tailwind v4, ESLint flat config, Prettier, Vitest. *AC:* `pnpm lint typecheck test build` pass.
- [ ] **build(web): OpenAPI export and typed client generation** — Build-time `openapi/v1.json`, `openapi-typescript` + `openapi-fetch` client, diff checks. *AC:* changing an endpoint without regenerating fails CI.
- [ ] **test(e2e): Playwright setup with mocked and fullstack projects** — MSW mock mode for app, compose-based fullstack project, one smoke test each. *AC:* both projects run in CI.
- [ ] **build(deploy): Dockerfiles for api, worker, web, landing** — Multi-stage, non-root, healthcheck command, multi-arch. *AC:* `docker compose -f deploy/docker-compose.yml up` serves health OK through `web`.
- [ ] **ci: GitHub Actions CI pipeline** — Jobs per workflow.md §5 with path filters and `ci-ok` aggregator. *AC:* PR shows single required check; caches effective (second run faster).
- [ ] **ci: PR title and branch name checks** — semantic PR title action, branch regex with exemptions (`claude/*`, `release-please--*`, `dependabot/*`, `renovate/*`). *AC:* bad title fails; `claude/x` branch passes.
- [ ] **ci: release-please and image publishing** — Single-version manifest, `release.yml` pushes GHCR images on tag. *AC:* merging release PR creates tag + images.
- [ ] **ci: Dependabot and CodeQL** — nuget, npm, actions, docker grouped updates; CodeQL C#/TS. *AC:* config validated, first scan runs.
- [ ] **build(deploy): Coolify staging environment** — Staging project, managed Postgres, deploy webhook on `main`. *AC:* `https://staging.app…/health/ready` returns 200 after merge.

## M1 — Auth, users, groups

- [ ] **feat(auth): ASP.NET Identity with cookie auth** — Register, login, logout, `GET /me`, email confirmation, password reset, lockout. *AC:* integration tests for each flow; cookie is `__Host-`, HttpOnly, Secure, SameSite=Lax.
- [ ] **feat(auth): CSRF protection for cookie clients** — Require `X-Requested-With: scal` on unsafe methods; CORS closed. *AC:* POST without header → 403 problem.
- [ ] **feat(auth): TOTP 2FA with recovery codes** — Enable/disable, login step. *AC:* login of 2FA user requires code; recovery code works once.
- [ ] **feat(auth): passkey registration and login** — .NET 10 Identity passkeys. *AC:* Playwright virtual authenticator test passes.
- [ ] **feat(api): problem details and error code catalogue** — RFC 9457 mapper, stable `code`s, validation errors format. *AC:* every error in tests is `application/problem+json` with `code`.
- [ ] **feat(worker): Postgres job queue and email sending** — `jobs` table, SKIP LOCKED runner, retries/backoff, MailKit sender, Mailpit in dev. *AC:* two worker instances never process the same job (test); verification email arrives in Mailpit in fullstack e2e.
- [ ] **feat(api): user profile settings** — Display name, locale, IANA time zone (validated), week start. *AC:* invalid zone → 422.
- [ ] **feat(api): groups CRUD with roles** — Create (creator = owner), rename, delete (owner only), list my groups. *AC:* role checks covered by authz matrix test.
- [ ] **feat(api): group membership management** — Change roles (admin ≤ admin, owner can promote owners), remove members, leave group, last-owner protection. *AC:* last owner cannot leave/demote self (409).
- [ ] **feat(api): group invites by email and link** — Role, expiry, max uses, pending invites for unregistered emails, accept endpoint. *AC:* invite → sign-up → auto-join flow passes in fullstack e2e.
- [ ] **feat(core): audit event recording** — `audit_events` written in same transaction for auth/group changes. *AC:* membership change produces audit row with before/after.
- [ ] **feat(api): rate limiting baseline** — Per IP for auth endpoints, per user for API. *AC:* 429 with `Retry-After` in test.

## M2 — Calendars, events, permission engine

- [ ] **feat(perm): permission engine core** — Pure `PermissionEngine` implementing permissions.md §4 (levels, tiers, floors, caps, scoped `everyone`/`anonymous`). *AC:* all worked examples A–F as table tests; property tests for invariants; 100 % branch coverage.
- [ ] **feat(api): calendars CRUD (user- and group-owned)** — Owner semantics, default time zone, `group_role_defaults`, `creators_manage_own_events`. *AC:* group admin can create group calendar; member cannot.
- [ ] **feat(api): calendar grants** — CRUD grants to users/groups(minRole); cannot grant above own level or `owner`. *AC:* `GET /calendars` returns `myLevel` correctly for all example principals.
- [ ] **feat(api): events CRUD for single events** — Timed/all-day with wall time + zone, ETag/If-Match, merge-patch, soft delete, `calendar_changes` log. *AC:* stale If-Match → 412; DST-gap time warns and shifts.
- [ ] **feat(api): event window query with permission filtering** — `GET /events?from&to&calendarIds`, free_busy field stripping, `none` excluded, "Shared with me". *AC:* Example B users see expected fields; query plan uses GiST index (EXPLAIN test on seeded 100k events, p95 < 150 ms).
- [ ] **feat(perm): event permission overrides API** — `GET/PUT /events/{id}/overrides` atomic replace, who-may-change rules, self-lockout guard, `has_overrides` + `acl_version` maintenance, audit. *AC:* contributor cannot hide event from manager (still manage); self-lockout → 409.
- [ ] **feat(perm): access explain endpoint** — `GET /events/{id}/access/explain?userId=` returning resolution steps. *AC:* output matches engine trace for examples.
- [ ] **feat(core): recurring events (RRULE/EXDATE/RDATE) storage and expansion** — Series master, `occurs_range`, expansion with caps, `expand=occurrences`. *AC:* weekly event across DST keeps local time; COUNT/UNTIL computed `series_until_utc`.
- [ ] **feat(api): occurrence edits (this / this and following / all)** — Exceptions, split series, re-keying. *AC:* golden tests for each mode incl. moved and cancelled instances.
- [ ] **test(api): authorization matrix test generator** — Enumerates OpenAPI operations and requires a matrix case per endpoint. *AC:* new endpoint without case fails CI.
- [ ] **feat(core): entitlement service skeleton** — `IEntitlementService` with plan limits from config; plan `free` hard-coded for now; enforce calendars/groups/members/override counts. *AC:* 11th event with overrides on Free → 402 `plan_limit_reached`.

## M3 — Web UI (MVP)

- [ ] **feat(web): app shell, routing, auth pages** — Login/register/2FA/passkey/reset pages, session handling, i18n (en, de), theme (light/dark/system). *AC:* axe finds no serious violations on auth pages.
- [ ] **feat(web): calendar views month/week/day/agenda** — FullCalendar integration with Temporal-based TZ handling, calendar sidebar with visibility and colors. *AC:* events render in user TZ; agenda view usable at 360 px width.
- [ ] **feat(web): event create/edit dialog** — All fields, all-day, time zone picker, recurrence editor (common presets + custom RRULE), edit-mode chooser for series. *AC:* mocked e2e covers create, edit "this and following", 412 conflict dialog.
- [ ] **feat(web): access indicators and read-only states** — Lock/pencil/busy badges, disabled controls by `access` capabilities, "Busy" rendering. *AC:* read-only user cannot open edit form; busy events show no details.
- [ ] **feat(web): event permission editor** — Override list editor (user/group+role/everyone/anonymous → level), preview "who sees what", explain dialog, plan usage meter. *AC:* fullstack e2e: Mia hides event, Vic sees busy, Adam still manages.
- [ ] **feat(web): calendar settings and sharing** — Grants, role defaults, share links, creators-manage toggle. *AC:* changes reflect in `myLevel` of other users (fullstack).
- [ ] **feat(web): groups and members management** — Create group, invite (email/link/QR), roles, leave/transfer. *AC:* invite link flow e2e.
- [ ] **feat(web): PWA install and offline shell** — Manifest, icons, service worker for shell. *AC:* Lighthouse PWA installable.
- [ ] **feat(web): paywall and limit UX** — Map `plan_limit_reached`/`feature_not_in_plan` to contextual upgrade dialogs. *AC:* mocked e2e shows dialog with correct numbers.

## M4 — iCal feeds

- [ ] **feat(ical): feed tokens API and UI** — Create/rotate/revoke tokens (calendar, aggregate, shared-with-me), webcal link + QR. *AC:* token shown once, stored hashed; revoked → 404.
- [ ] **feat(ical): personalised feed rendering** — Projection per permissions.md §7 / ical-caldav.md §1.3 with VTIMEZONE, RRULE + exceptions. *AC:* Verify golden files for busy/read/edit, all label modes, en/de.
- [ ] **feat(ical): ETag/304 and in-memory feed cache** — Cheap ETag query, `If-None-Match`, cache, compression. *AC:* unchanged feed returns 304 without event query (assert via EF interceptor count).
- [ ] **feat(ical): feed rate limiting and token redaction in logs** — 60/h per token; `/ical/` paths redacted. *AC:* log capture test shows no token.
- [ ] **feat(ical): anonymous share-link feeds** — `/ical/v1/s/{token}.ics`, capped at read. *AC:* event with `anonymous → none` override absent.
- [ ] **test(ical): cross-parser validation** — Parse output with ical.js in Vitest and Ical.Net. *AC:* CI fails on unparsable output.
- [ ] **docs: native app subscription guide** — Apple, Google, Outlook, Thunderbird with refresh caveats. *AC:* linked from feed UI.

**→ Public beta (MVP).**

## M5 — Plans, limits, billing

- [ ] **feat(billing): plans configuration and entitlement resolution** — `plans.json`, `plan_limits`, resource-owner-governs rule, self-host plan. *AC:* free user in Pro-owned group calendar can create overrides beyond 10.
- [ ] **feat(billing): Stripe checkout, portal and webhook sync** — `IBillingProvider`, Stripe implementation, idempotent webhook handling, `subscriptions` mirror, Stripe Tax. *AC:* Stripe CLI test events update plan; duplicate event ignored.
- [ ] **feat(billing): graceful downgrade** — Freeze over-limit calendars/sources, keep overrides enforced, block new. *AC:* downgrade test: overrides still hide events; new override → 402.
- [ ] **feat(billing): usage meters API and UI** — `/me/entitlements` with usage; settings page. *AC:* numbers match DB counts.
- [ ] **feat(billing): billing disabled mode** — `Billing__Provider=none` hides billing UI, all unlimited (overridable). *AC:* fullstack run with `none` has no billing routes.
- [ ] **feat(api): organizations and Team seats** — Orgs, members, seat counting (viewers free up to 10×), org-owned groups. *AC:* seat count excludes viewers; exceeding seats prompts quantity update.
- [ ] **feat(core): retention job per plan** — Trim audit/revisions; monthly partitions. *AC:* Free audit older than 7 days removed after grace.

## M6 — LLM import

- [ ] **feat(import): import sources CRUD and scheduling** — Cron clamped to plan, next run calc (Cronos), ToS attestation. *AC:* Free cannot set daily (402).
- [ ] **feat(import): SSRF-safe fetcher with robots.txt and politeness** — IP range blocking, DNS pinning, redirects, size/time caps, per-domain throttle, conditional GET. *AC:* unit tests for private ranges (v4/v6), rebinding; robots disallow → source error.
- [ ] **feat(import): ICS and JSON-LD fast paths** — Detect `text/calendar` and schema.org Event. *AC:* fixtures import without LLM call.
- [ ] **feat(import): HTML sanitizer and change detection** — AngleSharp cleaning, token budget truncation, content hash skip. *AC:* unchanged page → `skipped_unchanged`, no quota used.
- [ ] **feat(import): LLM extractor abstraction with Anthropic and OpenAI-compatible providers** — Structured output schema, prompt versioning, usage/cost recording, fake extractor. *AC:* fixture corpus passes with recorded responses.
- [ ] **feat(import): validation, dedupe and update detection** — Import keys, pg_trgm fuzzy duplicates, local-edits-win, removal after 2 misses. *AC:* re-running same page creates 0 new events; moved event updates.
- [ ] **feat(import): review queue API and UI** — Pending candidates, diff view, bulk accept/reject, edit-then-accept. *AC:* fullstack e2e with fake LLM: dry run → review → events appear.
- [ ] **feat(import): auto-publish with confidence threshold and anomaly guard** — Pro/Team only. *AC:* anomaly (3× count) forces review.
- [ ] **feat(import): ICS upload and ICS subscription import** — One-time upload + scheduled `ics.sync`. *AC:* Google export file imports with recurrence intact.
- [ ] **ci: import evaluation workflow** — Manual/weekly real-provider run reporting precision/recall. *AC:* report artifact uploaded.

## M7 — Landing page and launch readiness

- [ ] **feat(landing): marketing site with vite-ssg** — Hero (per-event permissions), feature sections, pricing table from `plans.json`, FAQ, de/en, SEO meta, OG images. *AC:* Lighthouse ≥ 95 all categories.
- [ ] **feat(landing): legal pages** — Imprint, privacy, terms, DPA download, bot page for importer. *AC:* linked from footer of app and landing.
- [ ] **feat(api): GDPR export and account deletion** — Async export zip (JSON + ICS), deletion with 14-day grace, calendar/group ownership hand-over. *AC:* deleting owner of group transfers or blocks with clear message.
- [ ] **feat(web): onboarding flow** — First calendar, invite group, subscribe feed checklist. *AC:* new user completes in < 2 min in usability test.
- [ ] **feat(api): reminders and notifications** — Per-user reminders (email, web push), in-app notifications, digests. *AC:* reminder fires within 1 min of due time in fullstack test.
- [ ] **feat(api): attendees and RSVP (internal)** — Invite users, RSVP, implicit read override. *AC:* invited outsider sees event in Shared with me.
- [ ] **feat(web): search and categories** — Full-text search within permissions, category filters. *AC:* search never returns `none` events (authz test).
- [ ] **feat(api): API tokens and webhooks** — Scoped PATs, signed webhooks with retries and delivery log. *AC:* signature verification sample passes; plan limits enforced.
- [ ] **feat(auth): OAuth login (Google, Microsoft, Apple)** — *AC:* account linking to existing email requires verification.
- [ ] **build(deploy): production environment and backups** — Prod Coolify project, backups to off-site S3, restore drill. *AC:* documented restore into staging succeeds.

**→ Paid launch (v1).**

## M8 — Hardening and CalDAV

- [ ] **feat(caldav): discovery and read-only CalDAV** — well-known, principals, PROPFIND, calendar-query, multiget, sync-collection, app passwords. *AC:* Apple Calendar and DAVx⁵ sync read-only incl. free/busy stripping.
- [ ] **feat(caldav): write support with per-event ACL enforcement** — PUT/DELETE with If-Match, privilege sets, 403 need-privileges, plan gate. *AC:* fixture tests per client; read-only event PUT → 403.
- [ ] **feat(web): availability view and conflict warnings** — *AC:* busy blocks respect permissions.
- [ ] **feat(api): event revisions and restore** — *AC:* restore creates new revision and audit entry.
- [ ] **feat(web): offline read cache** — *AC:* agenda of next 14 days visible offline.
- [ ] **feat(api): admin console** — Users, subscriptions, abuse flags, feature flags, import cost dashboard. *AC:* admin-only authz tested.
- [ ] **perf: load tests for feeds and window queries** — k6 scenarios. *AC:* 200 feed req/s with 90 % 304 on 2 vCPU api.
- [ ] **docs: operations runbook** — Incidents, restore, key rotation.

## M9+ — Later

- Native iOS/Android apps on OAuth2 PKCE (OpenIddict).
- Custom roles, nested groups, SSO (Team).
- Per-occurrence permission overrides.
- Attachments (S3), event comments, booking pages.
- Headless rendering for JS-heavy import sources; multi-page imports.

---

## Open questions for the owner

1. **Name**: keep "sCalenderPlus" (note: "Calender" spelling) or rename before the landing page/domains (M7)? Namespaces use `SCalenderPlus` until decided.
2. **License / self-host**: AGPL-3.0 open source (proposed), source-available, or closed with self-host only for paying customers?
3. **Pricing**: confirm Pro €12/month and Team €8/seat with free viewers; non-profit discount yes/no.
4. **Free override limit**: 10 active events with overrides — generous enough to hook, strict enough to convert? Validate in beta.
5. **Primary market/language**: DACH + EN at launch as assumed?
6. **LLM provider & data processing**: Anthropic as default sub-processor (EU data processing terms needed), and should the SaaS offer EU-only model hosting?
7. **Hosting provider and domains** for Coolify (e.g. Hetzner DE) and email provider (SMTP: Postmark/Brevo/SES EU).
8. **Default iCal label mode**: only read-only marked (`🔒`, proposed) or both read-only and editable marked?
