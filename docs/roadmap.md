# Roadmap

Milestones are ordered; each ends in a deployable state on staging. Each checklist item is sized to become **one GitHub issue / one PR** (title = conventional commit style PR title) and is listed in dependency order within its milestone. Format: **Title** — description. *AC:* acceptance criteria.

MVP (public beta) = M0–M4 (M4 includes beta readiness: prod, backups, legal, account deletion). Paid launch (v1) = M5–M7. Later = M8+. **ME** (editions and white-label) is a parallel track; its first items are done, the rest is scheduled after v1 unless the poly customer needs them earlier.

---

## M0 — Scaffolding and CI

- [ ] **chore: initialize monorepo layout** — Create `backend/`, `frontend/`, `e2e/`, `deploy/`, `.editorconfig`, `.gitignore`, `LICENSE` (per open question 2), root README linking docs, PR template, `CODEOWNERS` (permission engine path). *AC:* layout matches architecture/overview.md §2.
- [ ] **build(api): create .NET 10 solution and projects** — `SCalenderPlus.{Core,Application,Infrastructure,Api,Worker}` + test projects, `Directory.Build.props` (nullable, warnings as errors), central package management, typed options with `ValidateOnStart`. *AC:* `dotnet build` and `dotnet test` pass with one sample test each; missing required option → process exits non-zero.
- [ ] **test(core): add architecture rule tests** — NetArchTest rules: Core has no EF/ASP.NET refs; Api/Worker are only composition roots. *AC:* violating reference fails tests.
- [ ] **build(web): scaffold Vue 3 + Vite + TS app with pnpm workspace** — `frontend/app`, `packages/api-client`, `packages/ui`; Tailwind v4, ESLint flat config, Prettier, Vitest; runtime `/config.json` loading. *AC:* `pnpm lint typecheck test build` pass.
- [ ] **ci: GitHub Actions CI pipeline (backend + frontend)** — Jobs per workflow.md §5 for what exists so far, path filters, `ci-ok` aggregator; later items extend it. *AC:* PR shows single required check; caches effective (second run faster).
- [ ] **ci: PR title and branch name checks** — semantic PR title action with scope enum (`commitlint.config.mjs`), branch regex with exemptions (`claude/*`, `release-please--*`, `dependabot/*`, `renovate/*`, `gh-readonly-queue/*`), markdownlint, actionlint. *AC:* bad title fails; `claude/x` branch passes.
- [ ] **feat(db): EF Core + Npgsql + NodaTime setup with first migration** — `AppDbContext`, snake_case naming, `migrate` CLI command with advisory lock, `docker-compose.dev.yml` (Postgres + Mailpit). *AC:* migration applies to empty Testcontainers Postgres; `migrate` exits 0 twice in a row.
- [ ] **feat(api): health endpoints and structured logging** — `/health/live`, `/health/ready` (DB + migration version), `healthcheck` CLI command for chiseled images, JSON console logging, OpenTelemetry wiring (exporter optional). *AC:* integration test hits both endpoints; `healthcheck` exits 1 when DB down.
- [ ] **build(api): OpenAPI export and typed client generation** — Build-time `openapi/v1.json`, `openapi-typescript` + `openapi-fetch` client, diff checks, `oasdiff` job. *AC:* changing an endpoint without regenerating fails CI.
- [ ] **build(deploy): Dockerfiles for api, worker, web** — Multi-stage, non-root, healthcheck, multi-arch; `web` Caddyfile with security headers (CSP, HSTS, Referrer-Policy) and `/ical/` access-log redaction; `deploy/docker-compose.yml` with `migrate` one-shot. Landing image deferred to M7. *AC:* `docker compose -f deploy/docker-compose.yml up` serves health OK through `web`; trivy job passes.
- [ ] **test(e2e): Playwright setup with mocked and fullstack projects** — MSW mock mode for app; fullstack project runs against the compose stack using images built in the same CI run (passed as artifact/loaded). One smoke test each. *AC:* both projects run in CI and are part of `ci-ok`.
- [ ] **ci: release-please and image publishing** — `release-please-config.json` (`release-type: simple`, `bump-minor-pre-major`, `bump-patch-for-minor-pre-major`, `extra-files` with version markers). Images are built **in the release-please workflow when `release_created`** (tags created with `GITHUB_TOKEN` do not trigger other workflows) or via a GitHub App token. *AC:* merging release PR creates tag + GHCR images `X.Y.Z`.
- [ ] **ci: Dependabot and CodeQL** — nuget, npm, actions, docker grouped updates; CodeQL C#/TS. *AC:* config validated, first scan runs.
- [ ] **build(deploy): Coolify staging environment** — Staging project, Postgres, `deploy-staging` job pushes `:main-<sha>` and calls Coolify webhook on `main`; `migrate` marked `exclude_from_hc`. *AC:* `https://staging.app…/health/ready` returns 200 after merge.

## M1 — Auth, users, groups

- [ ] **feat(api): problem details and error code catalogue** — RFC 9457 mapper, stable `code`s, validation errors format. *AC:* every error in tests is `application/problem+json` with `code`.
- [ ] **test(api): authorization matrix test generator** — Enumerates OpenAPI operations and requires a matrix case per endpoint (before the first protected endpoint lands). *AC:* new endpoint without case fails CI.
- [ ] **feat(worker): Postgres job queue and email sending** — `jobs` table, SKIP LOCKED runner, retries/backoff, MailKit sender, Mailpit in dev. *AC:* two worker instances never process the same job (test); queued email arrives in Mailpit.
- [ ] **feat(auth): ASP.NET Identity with cookie auth** — Register, login, logout, `GET /me`, email confirmation, password reset, lockout; unverified-account restrictions (api.md §3). *AC:* integration tests for each flow; cookie is `__Host-`, HttpOnly, Secure, SameSite=Lax; unverified user creating a share link → 403 `email_not_verified`.
- [ ] **feat(auth): CSRF protection for cookie clients** — Require `X-Requested-With: scal` on unsafe methods; CORS closed. *AC:* POST without header → 403 problem.
- [ ] **feat(api): rate limiting baseline** — Per IP for auth/sign-up endpoints (+ disposable-email blocklist), per-session abuse limit, per-token plan limits. *AC:* 429 with `Retry-After` in test; normal UI navigation script never hits the session limit.
- [ ] **feat(auth): TOTP 2FA with recovery codes** — Enable/disable, login step. *AC:* login of 2FA user requires code; recovery code works once.
- [ ] **feat(core): audit event recording** — `audit_events` written in same transaction; used by all later mutations. *AC:* sample mutation produces audit row with before/after.
- [ ] **feat(api): user profile settings** — Display name, locale, IANA time zone (validated), week start. *AC:* invalid zone → 422.
- [ ] **feat(api): groups CRUD with roles** — Create (creator = owner + billing owner), rename, delete (owner only), list my groups. *AC:* role checks covered by authz matrix.
- [ ] **feat(api): group membership management** — Role changes per permissions §6.1 (admins cannot touch admins/owners), remove members, leave group, last-owner protection, billing-owner transfer. *AC:* last owner cannot leave/demote self (409); admin demoting another admin → 403.
- [ ] **feat(api): group invites by email and link** — Role (links ≤ member), expiry, max uses, pending invites bound to **verified** email, accept endpoint. *AC:* invite → sign-up → verify → auto-join passes in fullstack e2e; unverified account with matching email does not join.

## M2 — Calendars, events, permission engine

- [ ] **feat(perm): permission engine core** — Pure `PermissionEngine` implementing permissions.md §4 (levels, tiers, floors, restrict-only `everyone`/`anonymous`, override cap `edit`, link ceiling). *AC:* worked examples A–G as table tests; property tests for §8 invariants plus "no override yields `manage`" and "everyone/anonymous never elevate"; 100 % branch coverage.
- [ ] **feat(api): calendars CRUD (user- and group-owned)** — Owner semantics, default time zone, `group_role_defaults`, `creators_manage_own_events`, `creators_may_share_externally`, `acl_version` bumps. *AC:* group admin can create group calendar; member cannot.
- [ ] **feat(api): calendar grants** — CRUD grants to users/groups(minRole); cannot grant above own level or `owner`. *AC:* `GET /calendars` returns `myLevel` correctly for all example principals.
- [ ] **feat(api): events CRUD for single events** — Timed/all-day with wall time + zone, ETag/If-Match, merge-patch, soft delete, `calendar_changes` log, permission-aware query service + arch test forbidding direct `DbSet<Event>` use. *AC:* stale If-Match → 412; DST-gap time warns and shifts; arch test fails on direct access.
- [ ] **feat(api): event window query with permission filtering** — `GET /events?from&to&calendarIds`, free_busy field stripping, `none` excluded, "Shared with me", all-day boundary padding. *AC:* Example B users see expected fields; all-day event visible to viewers in UTC−10 and UTC+13; EXPLAIN on seeded 100k events uses GiST index, p95 < 150 ms.
- [ ] **feat(perm): event permission overrides API** — `GET/PUT /events/{id}/overrides` atomic replace, who-may-change rules incl. external-sharing rule and principal selection (§4.4), `has_overrides` + `acl_version` maintenance (calendar, named users/groups), audit. *AC:* contributor cannot hide event from manager; contributor adding external user → 403; `manage` override → 422.
- [ ] **feat(perm): access explain endpoint** — `GET /events/{id}/access/explain?userId=` returning resolution steps. *AC:* output matches engine trace for examples.
- [ ] **feat(api): move event between calendars** — `POST /events/{id}/move` with override re-validation, UID conflict, change log in both calendars (permissions §4.6). *AC:* move into calendar where an override would be external → 409 `override_invalid_in_target`.
- [ ] **feat(api): membership removal revokes event shares** — On group removal/demotion/grant removal delete `user:` overrides in affected calendars (opt-out flag), audit. *AC:* removed member no longer sees the previously individually shared event in API or feed.
- [ ] **feat(core): recurring events (RRULE/EXDATE/RDATE) storage and expansion** — Series master, `occurs_range`, expansion with caps, `expand=occurrences`. *AC:* weekly event across DST keeps local time; COUNT/UNTIL computed `series_until_utc`.
- [ ] **feat(api): occurrence edits (this / this and following / all)** — Exceptions, split series (keeps creator, copies overrides), re-keying. *AC:* golden tests for each mode incl. moved and cancelled instances; split by an `edit` user leaves creator unchanged.
- [ ] **feat(core): entitlement service skeleton** — `IEntitlementService` with plan limits from config; plan `free` for everyone for now; enforce calendars/groups/members/override counts. *AC:* 11th active event with overrides on Free → 402 `plan_limit_reached`; infinite series counts as active.

## M3 — Web UI (MVP)

- [ ] **feat(web): app shell, routing, i18n and theme** — Layout, router, session handling, vue-i18n (en, de), light/dark/system. *AC:* i18n key completeness test; axe clean on shell.
- [ ] **feat(web): auth pages** — Login/register/2FA/reset/verify-email pages. *AC:* axe finds no serious violations on auth pages; mocked e2e for 2FA step.
- [ ] **feat(web): calendar views month/week/day/agenda** — FullCalendar integration with Temporal-based TZ handling, calendar sidebar with visibility and colors (personal overlay). *AC:* events render in user TZ; agenda view usable at 360 px width; keyboard navigation of the grid.
- [ ] **feat(web): event create/edit dialog** — All fields, all-day, time zone picker, edit-mode chooser for series. *AC:* mocked e2e covers create, edit, 412 conflict dialog.
- [ ] **feat(web): recurrence editor** — Common presets + custom RRULE builder. *AC:* mocked e2e edits "this and following".
- [ ] **feat(web): access indicators and read-only states** — Lock/pencil/busy badges, disabled controls by `access` capabilities, "Busy" rendering. *AC:* read-only user cannot open edit form; busy events show no details.
- [ ] **feat(web): event permission editor** — Override list editor (user/group+role/everyone/anonymous → level ≤ edit), preview "who sees what", explain dialog, plan usage meter, "already-synced feeds can't be recalled" hint. *AC:* fullstack e2e: Mia hides event, Vic sees busy, Adam still manages.
- [ ] **feat(web): calendar settings and sharing** — Grants, role defaults, share links, creator toggles, move event. *AC:* changes reflect in `myLevel` of other users (fullstack).
- [ ] **feat(web): groups and members management** — Create group, invite (email/link/QR), roles, leave/transfer, revoke-shares option on removal. *AC:* invite link flow e2e.
- [x] **feat(web): PWA install and offline shell** — Edition manifest (runtime brand name via Caddy), icons, service worker precaching the app shell only (never api or `/ical/`), update prompt. *AC:* installable — Chromium reports no installability errors under the production CSP (the check behind Lighthouse's former PWA audit, removed in Lighthouse 12); shell loads offline.
- [x] **feat(web): paywall and limit UX** — Map `plan_limit_reached`/`feature_not_in_plan` to contextual upgrade dialogs (beta: "coming soon" instead of checkout), "not now" for 14 days per limit. *AC:* mocked e2e shows dialog with correct numbers.

## M4 — iCal feeds, ICS upload and beta readiness

- [x] **feat(ical): feed tokens API and UI** — Create/rotate/revoke tokens (calendar, aggregate, shared-with-me), webcal link + QR, rotate-all on password reset. *AC:* token shown once, stored hashed; revoked → 404 even when a cached render exists. *Status:* API (`/api/v1/feed-tokens`, `rotateFeedTokens` on password reset) and web UI ([ical-caldav.md §1.6](architecture/ical-caldav.md#16-web-ui): Settings → Calendar feeds, per-calendar subscribe, share links in calendar settings, `/e/{id}` routes, reset-page opt-in) built.
- [x] **feat(ical): personalised feed rendering** — Projection per permissions.md §7 / ical-caldav.md §1.3 with VTIMEZONE, RRULE + exceptions, UID rules. *AC:* golden files (committed `.ics` fixtures, no Verify in the repo) for busy (incl. stripped exceptions, opaque UID), read, edit, all label modes, en/de.
- [x] **feat(ical): ETag/304 and in-memory feed cache** — Token check first, ETag per ical-caldav.md §1.4, `If-None-Match`, cache, compression. *AC:* unchanged feed returns 304 without event query (EF interceptor count); override change on a shared-with-me event changes the ETag.
- [x] **feat(ical): feed rate limiting and token redaction in logs** — 60/h per token; `/ical/` paths redacted in app and proxy logs. *AC:* log capture test (app + Caddy) shows no token.
- [x] **feat(ical): anonymous share-link feeds** — `/ical/v1/s/{token}.ics`, link level as ceiling; with the share-links API (`/calendars/{id}/share-links`). *AC:* event with `anonymous → none` absent; `everyone → read` on a free_busy link still busy.
- [x] **test(ical): cross-parser validation** — Parse output with ical.js in Vitest and Ical.Net. *AC:* CI fails on unparsable output.
- [ ] **feat(import): one-time ICS upload** — Preview + commit, limits per ical-caldav.md §2. *AC:* Google Calendar export imports with recurrence and exceptions intact; re-upload of the same file creates 0 duplicates (UID match).
- [x] **docs: native app subscription guide** — Apple, Google, Outlook, Thunderbird with refresh caveats ([guides/calendar-apps.md](guides/calendar-apps.md); in the app at `/help/calendar-apps`, en/de). *AC:* linked from the app footer (and from the feed UI once it exists).
- [ ] **feat(api): account deletion** — `DELETE /me` with 14-day grace, lifecycle rules of permissions §4.6, blocks while sole owner of groups/calendars with members. *AC:* deleted user's feeds → 410; their user overrides gone; shared calendars survive.
- [x] **feat(web): legal pages (minimal)** — Imprint, privacy policy, terms; linked from app footer and sign-up; the deployment's `PUBLIC_BRAND_*_URL` pages when set, else built-in templates marked for the operator. *AC:* links present on all app pages.
- [ ] **build(deploy): production environment and backups** — Prod Coolify project, `release.yml` deploy, backups to off-site S3, restore drill, uptime monitor. *AC:* documented restore into staging succeeds and smoke e2e passes on it.

**→ Public beta (MVP).**

## ME — Editions and white-label (parallel track)

Spec: [product/editions.md](product/editions.md), [architecture/privacy-levels.md](architecture/privacy-levels.md), [architecture/sync.md](architecture/sync.md), [product/event-directory.md](product/event-directory.md).

- [x] **feat(web): runtime brand configuration** — `/config.json` `brand` object rendered by Caddy from `PUBLIC_BRAND_NAME`, `_LOGO_URL`, `_HUE`, `_SUPPORT_EMAIL`, `_IMPRINT_URL`, `_PRIVACY_URL`, `_TERMS_URL`; app name, logo, OKLCH hue palette and legal/support links from it, edition defaults when unset. *AC:* changing `PUBLIC_BRAND_HUE` and `PUBLIC_BRAND_NAME` on the same image changes palette and title after reload.
- [x] **build(web): edition builds** — `VITE_EDITION=standard|poly` (default `standard`), edition code in `src/editions/<edition>/` imported only via the `@edition` alias. *AC:* `VITE_EDITION=poly` build contains poly code; standard build does not import the poly folder.
- [x] **ci: edition leak check** — Build the standard edition and fail if a poly marker string appears in `dist/`. *AC:* adding a poly marker to shared code fails CI.
- [x] **feat(api): product name in emails** — `Brand__ProductName` (default "sCalenderPlus") and `Brand__SupportEmail` in email subjects and bodies. *AC:* email tests render the configured name.
- [x] **feat(billing): reverse trial and entitlements endpoint** — Pro limits for `Plans__TrialDays` (default 90, 0 disables) from `users.created_at`, only with `Billing__Provider=stripe`; resource owner governs; `GET /api/v1/me/entitlements` → `{ plan, trialEndsAt, limits }`. *AC:* new account on stripe creates the 11th override event; same account at day 91 → 402; `selfhost` unchanged.
- [x] **feat(events): cover titles** — `coverTitle` (nullable, trimmed, ≤ 200) settable with `edit`; `free_busy` viewers get it instead of "Busy", `read`+ get title and cover title; web event dialog and busy rendering. *AC:* `free_busy` response contains `coverTitle` but no title/description/location (authz test).
- [x] **feat(web): trial notice** — `GET /me/entitlements` as a query; "Pro trial: N days left" in the top bar while `trialEndsAt` is set (not for `selfhost`), dismissible per browser session, no checkout. *AC:* unit tests cover days left, plural forms (en/de), dismissal and no notice without a trial.
- [x] **fix(ui): brand hue contrast** — Text-bearing brand steps darken for green to cyan hues (`--scal-brand-shade`). *AC:* Vitest checks every text-bearing token pair for hues 0–360 (step 5) in light and dark mode at ≥ 4.5:1.
- [x] **build(web): edition title and favicon** — The build writes the edition's name into `index.html` and emits its favicon (poly: a discreet calendar glyph). *AC:* standard and poly builds pass the leak check; the poly favicon is not in the standard `dist/`.
- [x] **build(deploy): per-edition web image** — `web.Dockerfile` build arg `EDITION` (the image knows its name and refuses to start under another `WEB_IMAGE`); publish `scalenderplus-poly-web` alongside `scalenderplus-web` in `images.yml` and releases (`build-images.yml` matrix); compose files take the web image name from `WEB_IMAGE` (default `scalenderplus-web`). *AC:* both images published; the leak check runs on each image's own assets in its build stage (`check:edition-leak --edition $EDITION`) and on both CI builds.
- [x] **feat(web): poly wording overlay** — Poly-only locale JSON (`src/editions/poly/locales/`) merged over base messages ("group" → "constellation" / "Konstellation"). *AC:* `editions.spec.ts` checks the overlay (only existing keys, same placeholders, no "group"/"Gruppe" left in the merged poly messages); standard build unchanged (leak check).
- [ ] **feat(billing): trial-end reminder** — Email and in-app banner 7 days before `trialEndsAt`, once per account. *AC:* worker test sends exactly one email at day 83 with `Plans__TrialDays=90`.
- [x] **feat(ical): cover titles in feeds** — `free_busy` VEVENTs use `coverTitle` as `SUMMARY` (part of M4 feed rendering). *AC:* golden file for a busy event with cover title.
- [ ] **feat(auth): special-category consent (poly)** — Explicit Art. 9 consent at sign-up, recorded with timestamp and text version. *AC:* sign-up without consent → 422; consent text version stored.
- [ ] **docs(poly): DPIA and sub-processor list** — Data protection impact assessment and minimal sub-processor list for the poly deployment. *AC:* reviewed by the DPIA owner (open question 15) before poly launch.
- [ ] **feat(web): discretion features (poly)** — Neutral app name/icon option (manifest), discreet notification/email texts, quick-hide; prominent session revoke and feed rotation. *AC:* with discreet mode on, no event title or person name appears in push or email payloads (test).

Later in this track:

- [ ] **build(api): edition-specific backend assemblies and images** — Poly-only modules in separate assemblies referenced only by `scalenderplus-poly-api`/`-worker`. *AC:* standard api image contains no poly assembly (CI check).
- [ ] **feat(api): real-time updates for own clients** — SSE (or websocket) stream of change notifications from `calendar_changes`, permission-filtered; web app refetches affected windows. *AC:* second browser sees an edit within 2 s (fullstack e2e).
- [ ] **feat(sync): Google Calendar and Microsoft Graph write-through** — Connect account, dedicated target calendar, per-user projection incl. busy-only/cover titles, reconcile job. *AC:* access revoked → event removed from the connected calendar on the next run.
- [ ] **feat(sync): Google and Microsoft import connectors** — Two-way: import the user's own calendars with ICS-subscription dedupe. *AC:* re-import creates 0 duplicates.
- [ ] **feat(privacy): privacy level B (content encrypted)** — Per-user key pairs, device pairing (QR, safety numbers), recovery key, event keys wrapped for `read`+, re-key on revoke, raise-only level. *AC:* server DB contains no plaintext title for a level-B event; `free_busy` viewer gets no wrapped key.
- [ ] **feat(privacy): privacy level A (end-to-end)** — Only if demand is proven; client-side recurrence, reminders, search. *AC:* feeds/CalDAV/sync refuse level-A calendars.
- [ ] **feat(directory): event directory (poly)** — Regional calendars, organiser verification and claiming, moderation queue, moderator platform role, reports/takedown/bans/audit, going/interested with per-event visibility. *AC:* unverified submission is invisible until approved; moderator cannot read private calendars (authz test).
- [ ] **feat(directory): adult add-on (poly)** — Opt-in with age verification, web-only, separate payment provider. *AC:* store/PWA-store builds cannot unlock it; without the add-on explicit listings are hidden.

## M5 — Plans, limits, billing

- [ ] **feat(billing): plans configuration and entitlement resolution** — `plans.json`, `plan_limits`, resource-owner-governs rule (billing owner), editors limit, self-host plan. *AC:* free user in Pro-owned group calendar can create overrides beyond 10.
- [ ] **feat(billing): Stripe checkout, portal and webhook sync** — `IBillingProvider`, Stripe implementation, idempotent webhook handling, `subscriptions` mirror, Stripe Tax. *AC:* Stripe CLI test events update plan; duplicate event ignored.
- [ ] **feat(billing): graceful downgrade** — Full table in plans.md (freeze calendars/groups, keep overrides enforced, disable webhooks, pause sources). *AC:* downgrade test: overrides still hide events; new override → 402; frozen calendar allows delete but not edit.
- [ ] **feat(billing): usage meters API and UI** — `/me/entitlements` with usage (per-creator breakdown for overrides); settings page. *AC:* numbers match DB counts.
- [ ] **feat(billing): billing disabled mode** — `Billing__Provider=none` hides billing UI, all unlimited (overridable). *AC:* fullstack run with `none` has no billing routes.
- [ ] **feat(core): retention job per plan** — Trim audit/revisions, purge soft-deleted events > 90 days; monthly partitions. *AC:* Free audit older than 7 days removed after grace.
- [ ] **feat(web): event and calendar history view** — Audit-log-based history (`GET /audit`) with plan retention and upgrade prompt. *AC:* history older than retention not returned (authz + plan test).
- [ ] **feat(api): organizations** — Orgs, members, org-owned groups and billing subject. *AC:* group moved into org uses org plan.
- [ ] **feat(billing): Team seats** — Seat counting per plans.md (content editors via any path; read-only free up to 10×), Stripe quantity sync. *AC:* viewer with an individual `edit` grant counts as a seat.

## M6 — LLM import

- [ ] **feat(import): import sources CRUD and scheduling** — Cron clamped to plan, next run calc (Cronos), ToS attestation, verified email required. *AC:* Free cannot set daily (402).
- [ ] **feat(import): SSRF-safe fetcher with robots.txt and politeness** — IP range blocking, DNS pinning, redirects, size/time caps, per-domain throttle, conditional GET. *AC:* unit tests for private ranges (v4/v6), rebinding; robots disallow → source error.
- [ ] **feat(import): ICS and JSON-LD fast paths** — Detect `text/calendar` and schema.org Event. *AC:* fixtures import without LLM call.
- [ ] **feat(import): HTML sanitizer and change detection** — AngleSharp cleaning, token budget truncation, content hash skip. *AC:* unchanged page → `skipped_unchanged`, no quota used.
- [ ] **feat(import): LLM extractor abstraction with Anthropic and OpenAI-compatible providers** — Structured output schema, prompt versioning, usage/cost recording, fake extractor + fake LLM server for compose. *AC:* fixture corpus passes with recorded responses.
- [ ] **feat(import): LLM cost guards** — Per-subject quotas (Free values during the reverse trial, plans.md principle 1), dry-run caps per user and subject, global daily Free budget, cost alerting. *AC:* exhausted Free budget defers HTML runs, ICS runs still execute; a trialing account gets the Free LLM runs per month.
- [ ] **feat(import): validation, dedupe and update detection** — Import keys, pg_trgm fuzzy duplicates, local-edits-win, removal after 2 misses. *AC:* re-running same page creates 0 new events; moved event updates.
- [ ] **feat(import): review queue API and UI** — Pending candidates, diff view, bulk accept/reject, edit-then-accept. *AC:* fullstack e2e with fake LLM: dry run → review → events appear.
- [ ] **feat(import): auto-publish with confidence threshold and anomaly guard** — Pro/Team only. *AC:* anomaly (3× count) forces review.
- [ ] **feat(import): ICS subscription import** — Scheduled `ics.sync` reusing upload parser and dedupe. *AC:* changed upstream event updates, removed one is cancelled after 2 misses.
- [ ] **ci: import evaluation workflow** — Manual/weekly real-provider run reporting precision/recall. *AC:* report artifact uploaded.

## M7 — Landing page and launch readiness

- [x] **feat(landing): marketing site with vite-ssg** — Landing Dockerfile + Coolify resource, hero (per-event permissions), feature sections, pricing table from `plans.json`, FAQ, de/en, SEO meta, OG images. *AC:* Lighthouse ≥ 95 all categories.
- [x] **feat(landing): legal pages and bot page** — Full terms, DPA download, importer bot page. *AC:* linked from footer of app and landing.
- [ ] **feat(api): GDPR export** — Async export zip (JSON + ICS). *AC:* export contains all own events and memberships, no events of others below `read`.
- [ ] **feat(api): calendar transfer between user and group** — `POST /calendars/{id}/transfer` with plan check and warning. *AC:* transfer to Free owner over limit → frozen per downgrade rules.
- [x] **feat(web): onboarding flow** — First calendar, invite group, subscribe feed checklist. *AC:* new user completes in < 2 min in usability test.
- [ ] **feat(api): reminders** — Per-user reminders (email), `reminder.dispatch` job, only `Le ≥ read`. *AC:* reminder fires within 1 min of due time in fullstack test.
- [ ] **feat(web): web push for reminders** — VAPID, push subscriptions. *AC:* push delivered in Chromium e2e with mocked push service.
- [ ] **feat(api): in-app notifications and email digests** — Permission evaluated at send time. *AC:* user who lost access gets no digest entry for that event.
- [ ] **feat(api): attendees and RSVP (internal)** — Invite users, RSVP, implicit read override under override rights. *AC:* `edit` user inviting an outsider → 403; manager invite → outsider sees event in Shared with me.
- [ ] **feat(web): full-text search** — Within permissions, `Le ≥ read` only. *AC:* search never matches `none` or `free_busy` events (authz test).
- [ ] **feat(web): categories and filters** — *AC:* category filter persists per user and appears in feeds as CATEGORIES.
- [ ] **feat(api): personal API tokens** — Scoped PATs, plan limits. *AC:* scope without `events:write` → 403 on POST /events.
- [ ] **feat(api): webhooks** — Signed webhooks with retries and delivery log, evaluated as owner. *AC:* signature verification sample passes; plan limits enforced.
- [ ] **feat(auth): passkeys** — .NET 10 Identity passkeys. *AC:* Playwright virtual authenticator test passes.
- [ ] **feat(auth): session management** — List/revoke sessions, "sign out everywhere" with feed-token rotation option. *AC:* revoked session cookie → 401.
- [ ] **feat(auth): OAuth login (Google, Microsoft, Apple)** — *AC:* account linking to existing email requires verification.
- [ ] **feat(web): embeddable calendar widget** — Share-link based iframe, "Powered by" on Free, abuse report link. *AC:* widget shows only what the link level allows.

**→ Paid launch (v1).**

## M8 — Hardening and CalDAV

- [ ] **feat(caldav): discovery and principals** — well-known, `current-user-principal`, calendar-home-set, app passwords. *AC:* Apple Calendar and DAVx⁵ discover calendars.
- [ ] **feat(caldav): read-only sync** — PROPFIND, calendar-query, multiget, sync-collection (incl. deletes when access drops). *AC:* free/busy stripping identical to feeds (shared golden files).
- [ ] **feat(caldav): write support with per-event ACL enforcement** — PUT/DELETE with If-Match, privilege sets, 403 need-privileges, plan gate. *AC:* fixture tests per client; read-only event PUT → 403.
- [ ] **feat(api): incremental changes endpoint** — `/changes?since=` with access-loss deletes. *AC:* event restricted to `none` appears as delete for that user.
- [ ] **feat(web): availability view and conflict warnings** — *AC:* busy blocks respect permissions.
- [ ] **feat(api): event revisions and restore** — *AC:* restore creates new revision and audit entry.
- [ ] **feat(web): offline read cache** — *AC:* agenda of next 14 days visible offline.
- [ ] **feat(api): admin console** — Users, subscriptions, abuse flags, feature flags, import cost dashboard. *AC:* admin-only authz tested.
- [ ] **perf: load tests for feeds and window queries** — k6 scenarios. *AC:* 200 feed req/s with 90 % 304 on 2 vCPU api.
- [ ] **docs: operations runbook** — Incidents, restore, key rotation, abuse takedown. *AC:* restore drill follows the runbook verbatim.

## M9+ — Later

- Native iOS/Android apps on OAuth2 PKCE (OpenIddict); `Idempotency-Key`.
- Custom roles, nested groups, SSO (Team).
- Per-occurrence permission overrides; event share links (anonymous single-event sharing).
- Attachments (S3), event comments, booking pages, calendar archive, iTIP for external attendees.
- Headless rendering for JS-heavy import sources; multi-page imports.

---

## Open questions for the owner

1. **Name**: keep "sCalenderPlus" (note: "Calender" spelling) or rename before the landing page/domains (M7)? Namespaces use `SCalenderPlus` until decided.
2. **License / self-host**: AGPL-3.0 open source (proposed), source-available, or closed with self-host only for paying customers? Needed for the `LICENSE` file in M0.
3. **Pricing**: confirm Pro €12/month and Team €8/seat with free read-only people; non-profit discount yes/no.
4. **Free override limit**: 10 active events with overrides — generous enough to hook, strict enough to convert? A family using "private" entries may hit it within weeks (Fiona persona). Validate in beta.
5. **Primary market/language**: DACH + EN at launch as assumed?
6. **LLM provider & data processing**: Anthropic as default sub-processor (EU data processing terms needed), and should the SaaS offer EU-only model hosting?
7. **Hosting provider and domains** for Coolify (e.g. Hetzner DE) and email provider (SMTP: Postmark/Brevo/SES EU).
8. **Default iCal label mode**: only read-only marked (`🔒`, proposed) or both read-only and editable marked?
9. **Editors limit on Pro** (proposed 25): accept, tune, or drop?
10. **Team at paid launch?** Organizations + seats (two M5 items) could slip to after v1, launching with Free + Pro only.
11. **External sharing by creators**: keep default `creatorsMayShareExternally = false`?
12. **Poly edition name and brand**: product name, domain, logo and hue for the poly deployment (needed before its first deployment; the code only uses the edition key `poly`).
13. **Adult add-on payment provider**: which provider that accepts adult businesses (Stripe stays for the core)?
14. **Trial length**: confirm 90 days (`Plans__TrialDays`). (Decided: trial accounts keep the Free LLM import quotas, [plans.md](product/plans.md) principle 1.)
15. **DPIA owner**: who on the customer side owns the DPIA and Art. 9 consent texts for the poly edition?
16. **Poly Free limits**: confirm the proposal in plans.md (events with overrides unlimited or 50, 10 entries per event, 15 members per group).
