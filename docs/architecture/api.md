# REST API (v1)

The web app, future native apps and third-party integrators use **the same public API**. There is no private "BFF-only" API — everything the UI can do is documented and versioned.

## 1. Conventions

| Topic | Decision |
|---|---|
| Base path | `/api/v1`. Breaking changes → `/api/v2` (both served in parallel ≥ 12 months). Additive changes are non-breaking. |
| Format | JSON, `camelCase`, UTF-8. Enums as lowercase strings (`"free_busy"`). |
| IDs | UUIDv7 strings. |
| Time values | Timed: `{"dateTime":"2026-11-02T18:00:00","timeZone":"Europe/Berlin"}`; all-day: `{"date":"2026-11-02"}`. Instants (`createdAt`) are RFC 3339 UTC. Ranges in queries: `from`/`to` RFC 3339 instants. |
| Concurrency | Every mutable resource returns `ETag`; `PATCH`/`PUT`/`DELETE` require `If-Match` (missing → `428`, stale → `412`). |
| Partial update | `PATCH` with JSON Merge Patch (`application/merge-patch+json`). |
| Idempotency (v1) | `POST` accepts optional `Idempotency-Key` header (stored 24 h per principal) — needed once native/mobile clients exist; not in MVP. |
| Pagination | Cursor-based: `?limit=50&cursor=…` → response `{ "items": [...], "nextCursor": "…" \| null }`. Max limit 200. Opaque cursor = base64url of (sort key, id). Exception: event window queries return the whole window (bounded by window size limits). |
| Filtering/sorting | Explicit query params per endpoint (`?calendarIds=…&q=…`); no generic query language. |
| Rate limits | Plan limits (plans.md) apply to **bearer tokens** only; cookie sessions (the web app) get a generous per-user abuse limit (e.g. 600/min) so normal UI use never hits Free's 60/min; auth endpoints per IP. Headers `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset`; `429` + `Retry-After`. |
| Deprecation | `Deprecation` and `Sunset` headers; listed in changelog. |
| Localization | `Accept-Language` affects problem `title`/`detail` texts only; data is never localized. |

## 2. Errors — RFC 9457 Problem Details

All errors are `application/problem+json`:

```json
{
  "type": "https://scalenderplus.app/problems/plan-limit-reached",
  "title": "Plan limit reached",
  "status": 402,
  "detail": "Your plan allows 10 events with custom permissions. Upgrade to Pro for unlimited.",
  "instance": "/api/v1/events/0192f.../overrides",
  "code": "plan_limit_reached",
  "limit": { "key": "events_with_overrides", "max": 10, "used": 10 },
  "traceId": "00-4bf92f..."
}
```

| Status | `code` examples |
|---|---|
| 400 | `validation_failed` (+ `errors: { "field": ["msg"] }`) |
| 401 | `unauthenticated`, `token_expired` |
| 403 | `insufficient_permission` (+ `required`, `actual` levels), `two_factor_required`, `external_sharing_not_allowed`, `email_not_verified` |
| 404 | `not_found` (also for `none`-level resources — no existence leaks) |
| 409 | `conflict`, `permission_self_lockout` (defensive), `calendar_frozen`, `override_invalid_in_target`, `uid_conflict` |
| 402 | `plan_limit_reached`, `feature_not_in_plan` (+ `limit`/`feature`) |
| 412 / 428 | `precondition_failed`, `precondition_required` |
| 422 | `recurrence_invalid`, `time_zone_invalid` |
| 429 | `rate_limited` |

`code` values are stable and part of the contract; the frontend maps them to i18n messages and upgrade prompts. Implemented with ASP.NET Core `AddProblemDetails` + a domain-exception → problem mapper.

## 3. Authentication

| Client | Mechanism |
|---|---|
| Web app (same-origin) | Cookie session (`__Host-scal`, HttpOnly, Secure, SameSite=Lax) issued by Identity; CSRF: unsafe methods require header `X-Requested-With: scal` (cannot be set cross-site without CORS preflight; CORS is closed). |
| Integrators, CLI | `Authorization: Bearer scal_pat_<random>` personal access tokens with scopes. |
| Native apps (later) | OAuth 2.1 authorization code + PKCE via OpenIddict; access token (15 min) + rotating refresh token. |
| iCal feeds | Secret token in URL (see ical-caldav.md). |
| CalDAV (later) | Basic auth with app passwords. |

Token scopes: `calendars:read`, `calendars:write`, `events:read`, `events:write`, `groups:read`, `groups:write`, `imports:write`, `webhooks:write`, `account:read`. Token actions are additionally limited by the user's permissions (scopes never elevate).

Unverified accounts can sign in and use their own calendars, but cannot accept email invites/pending shares, invite others, create share links or import sources (`403 email_not_verified`).

Auth endpoints (`/api/v1/auth/…`): `register`, `login` (password → may answer `{ "twoFactorRequired": true }`), `login/2fa`, `logout`, `passkeys/options` + `passkeys/login` (v1), `confirm-email`, `forgot-password`, `reset-password`, `external/{provider}` (v1), `sessions` (list/revoke), `me`.

## 4. Resource overview

| Resource | Endpoints |
|---|---|
| **Me** | `GET/PATCH /me`, `GET /me/entitlements` (plan, limits, usage), `DELETE /me` (MVP, 14-day grace), `GET /me/export` (v1, GDPR, async job) |
| **Groups** | `GET/POST /groups`, `GET/PATCH/DELETE /groups/{id}`, `GET /groups/{id}/members`, `PATCH/DELETE /groups/{id}/members/{userId}`, `POST /groups/{id}/invites`, `GET /groups/{id}/invites`, `DELETE /invites/{id}`, `POST /invites/{token}/accept`, `POST /groups/{id}/transfer` |
| **Calendars** | `GET /calendars` (all visible, with `myLevel`), `POST /calendars`, `GET/PATCH/DELETE /calendars/{id}`, `POST /calendars/{id}/transfer`, `POST /calendars/{id}/archive` |
| **Calendar grants** | `GET/POST /calendars/{id}/grants`, `PATCH/DELETE /calendars/{id}/grants/{grantId}` |
| **Share links** | `GET/POST /calendars/{id}/share-links`, `DELETE /share-links/{id}` |
| **My calendar prefs** | `PUT /calendars/{id}/prefs` (hidden, color, default reminders) |
| **Events** | `GET /events?from&to&calendarIds&expand=occurrences` (window), `POST /events`, `GET/PATCH/DELETE /events/{id}`, `POST /events/{id}/move` (`{targetCalendarId}`, permissions §4.6), `GET /events/search?q=` (v1, only `Le ≥ read`) |
| **Occurrences** | `PATCH /events/{id}/occurrences/{recurrenceId}` (this), `POST /events/{id}/split` (this and following), `DELETE /events/{id}/occurrences/{recurrenceId}` |
| **Event overrides** | `GET /events/{id}/overrides`, `PUT /events/{id}/overrides` (replace full set, atomic), `GET /events/{id}/access` (my level + capabilities), `GET /events/{id}/access/explain?userId=` |
| **Attendees/RSVP** (v1) | `POST/DELETE /events/{id}/attendees`, `POST /events/{id}/rsvp` |
| **Reminders** | `GET/PUT /events/{id}/reminders` (mine) |
| **Revisions** (v1) | `GET /events/{id}/revisions`, `POST /events/{id}/revisions/{rev}/restore` |
| **Availability** (v1) | `POST /availability` (principals + window → busy blocks, respecting permissions) |
| **Feeds** | `GET/POST /feed-tokens`, `PATCH/DELETE /feed-tokens/{id}`, `POST /feed-tokens/{id}/rotate` |
| **ICS upload** (MVP) | `POST /calendars/{id}/ics-upload?dryRun=true\|false` |
| **Imports** (v1) | `GET/POST /calendars/{id}/import-sources`, `GET/PATCH/DELETE /import-sources/{id}`, `POST /import-sources/{id}/dry-run`, `POST /import-sources/{id}/run`, `GET /import-sources/{id}/runs`, `GET /import-candidates?state=pending`, `POST /import-candidates/decide` (bulk) |
| **Changes** (later, with CalDAV/native apps) | `GET /changes?since={seq}&calendarIds=` (incremental sync; must also emit deletes when an event becomes `none` for the caller) |
| **Notifications** (v1) | `GET /notifications`, `POST /notifications/read`, `PUT /me/push-subscriptions` |
| **API tokens** (v1) | `GET/POST /api-tokens`, `DELETE /api-tokens/{id}` |
| **Webhooks** (v1) | `GET/POST /webhooks`, `PATCH/DELETE /webhooks/{id}`, `GET /webhooks/{id}/deliveries`, `POST /webhooks/{id}/test` |
| **Billing** (v1) | `GET /billing/subscription`, `POST /billing/checkout-session`, `POST /billing/portal-session`; `POST /billing/webhooks/stripe` (provider callback, outside `/api/v1` versioning) |
| **Audit** | `GET /audit?resourceType&resourceId&cursor` (managers; retention per plan) |
| **Organizations** (v1, Team) | `GET/POST /orgs`, members, seats, groups |
| **System** | `GET /health/live`, `GET /health/ready`, `GET /openapi/v1.json` (root, unversioned) |

### Event representation (excerpt)

```json
{
  "id": "0192f2c4-…",
  "calendarId": "0192f2a1-…",
  "uid": "0192f2c4-…@scalenderplus",
  "title": "Board meeting",
  "start": { "dateTime": "2026-11-02T18:00:00", "timeZone": "Europe/Berlin", "utc": "2026-11-02T17:00:00Z" },
  "end":   { "dateTime": "2026-11-02T20:00:00", "timeZone": "Europe/Berlin", "utc": "2026-11-02T19:00:00Z" },
  "allDay": false,
  "recurrence": { "rrule": "FREQ=MONTHLY;BYDAY=1MO", "exdates": [] },
  "access": { "level": "read", "canEdit": false, "canManage": false, "reason": "calendar_grant" },
  "hasOverrides": true,
  "createdBy": { "id": "…", "displayName": "Adam" },
  "etag": "W/\"8f3a\""
}
```

For `free_busy` events the API returns only `id`, `calendarId`, `start`, `end`, `allDay`, `transparency`, `access`, with `"title": null` — the client renders the localized "Busy". Fields are **omitted server-side**, never sent and hidden client-side.

Window query `GET /events?from=…&to=…&expand=occurrences` returns expanded occurrences (`occurrenceId = {eventId}:{recurrenceIdUtc}`) for calendar views; without `expand` it returns series masters (for sync clients). Max window: 13 months.

## 5. OpenAPI generation

- Generated at build time by `Microsoft.AspNetCore.OpenApi` (OpenAPI 3.1) from endpoint metadata (`.WithName`, `.Produces<T>`, `.ProducesProblem`, typed results `Results<Ok<T>, NotFound, ProblemHttpResult>`).
- Document transformers add: security schemes, problem `code` enum, examples, `x-plan-gated` extension on gated operations.
- **Build-time export**: `dotnet build` with `Microsoft.Extensions.ApiDescription.Server` writes `backend/openapi/v1.json`, which is **committed**. CI fails if the generated file differs from the committed one (contract changes are visible in PR diffs); an integration test also asserts that the document served at `/openapi/v1.json` equals the committed file. During build-time generation the api runs inside the `GetDocument.Insider` tool with placeholder values for required options (nothing connects to them). The document's `info.version` is the fixed `v1` (not the product version) so release bumps cause no drift. Container builds skip the export (`-p:OpenApiGenerateDocuments=false`).
- System endpoints (`/health/live`, `/health/ready`) are minimal API endpoints with OpenAPI metadata (tag `System`), so they are in the document and the typed client.
- **Breaking-change check**: CI job `openapi-breaking` runs `oasdiff breaking` (`tufin/oasdiff` image) between the PR's `v1.json` and the PR base branch's; breaking changes fail the build unless the PR carries the label `api-breaking-approved` (adding/removing the label re-runs CI). On pushes the result is informational only. The report is written to the job summary.
- Docs UI: Scalar at `/docs` (dev and staging; prod optional via `Api__PublicDocs=true`).

## 6. Typed frontend client

```text
backend/openapi/v1.json
   └─ pnpm -C frontend --filter @scalenderplus/api-client generate
        ├─ openapi-typescript → src/schema.d.ts (types only, then prettier)
        └─ src/client.ts       → createApiClient(): createClient<paths>() from openapi-fetch (hand-written)
                                  + middleware: X-Requested-With (done); problem+json → ApiError, auth refresh (native) later
```

- Usage: `const { data, error } = await api.GET('/api/v1/events', { params: { query: { from, to } } })` — paths, params and responses fully typed; renames break the TS build.
- Vue integration: thin composables per resource (`useEvents(range)`, `useUpdateEvent()`) wrapping TanStack Query with query keys and invalidation rules; optimistic updates for drag & drop with rollback on `412`.
- **MSW mock handlers** live next to the client (`src/mocks/handlers.ts`, export `@scalenderplus/api-client/mocks`) and are typed against the same `paths` via `openapi-msw`, so mocks break when the contract changes. Used by: dev mock mode (`pnpm --filter app dev:mock`, i.e. `vite --mode mock` with the `msw/vite` plugin serving the worker script), Vitest component tests, Playwright mocked e2e.
- The generated schema is regenerated in CI and diff-checked (like `v1.json`).
- Future native apps generate their clients from the same document (Swift OpenAPI Generator / Kotlin openapi-generator).

## 7. Webhooks (v1)

- Events: `event.created|updated|deleted`, `calendar.updated`, `import.run.completed`, `import.review.pending`.
- Payload: `{ "id", "type", "occurredAt", "data": { … resource as seen by the webhook owner … } }` — permissions are evaluated **as the webhook owner**, so webhooks never leak more than the owner could read.
- Signature header `Scal-Signature: t=<unix>,v1=<hex hmac-sha256(secret, t + "." + body)>`; 5-minute tolerance; retries with exponential backoff for 24 h; delivery log visible.
