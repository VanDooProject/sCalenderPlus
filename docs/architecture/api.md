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
| Rate limits | Plan limits (plans.md) apply to **bearer tokens** only; cookie sessions (the web app) get a generous per-user abuse limit (600/min, sliding) so normal UI use never hits Free's 60/min; auth endpoints per client IP (login, 2FA, confirmation, password reset: 10/min; sign-up: 5/h; IPv6 grouped by /64). `429 rate_limited` + `Retry-After` (seconds). Limits are in memory per api replica and configurable (`RateLimiting__*`, `Api/RateLimiting`). `RateLimit-Limit`/`-Remaining`/`-Reset` headers come with the token limits (v1). Sign-up also rejects disposable-email domains (`422 email_domain_not_allowed`; bundled list + `SignUp__BlockedEmailDomains`). |
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

| Status | `code` |
|---|---|
| 400 | `validation_failed` (+ `errors: { "field": ["msg"] }`, keys are the camelCase JSON member paths), `bad_request` (malformed request: invalid JSON, wrong parameter type), `token_invalid` (email confirmation / password reset link invalid, expired or used) |
| 401 | `unauthenticated`, `token_expired`, `invalid_credentials` (login: unknown email, wrong password or second factor, locked out — deliberately indistinguishable) |
| 403 | `insufficient_permission` (+ `required`, `actual` levels), `two_factor_required`, `external_sharing_not_allowed`, `email_not_verified`, `csrf_header_missing`, `reauthentication_failed` (wrong password/code when confirming a sensitive change) |
| 404 | `not_found` (also for `none`-level resources — no existence leaks; also unknown routes) |
| 405 | `method_not_allowed` |
| 409 | `conflict`, `permission_self_lockout` (defensive), `calendar_frozen`, `override_invalid_in_target`, `uid_conflict` |
| 402 | `plan_limit_reached`, `feature_not_in_plan` (+ `limit`/`feature`) |
| 412 / 428 | `precondition_failed`, `precondition_required` |
| 413 / 415 | `payload_too_large`, `unsupported_media_type` |
| 422 | `recurrence_invalid`, `time_zone_invalid`, `email_domain_not_allowed` (both + `errors` with the field, like `validation_failed`) |
| 429 | `rate_limited` |
| 500 / 503 | `internal_error` (no details; correlate via `traceId`), `service_unavailable` |

`code` values are stable and part of the contract; the frontend maps them to i18n messages and upgrade prompts.

Implementation:

- **Catalogue**: `SCalenderPlus.Application.Errors.ErrorCodes` holds every code (constants); `SCalenderPlus.Api.Problems.ProblemCatalogue` gives each exactly one status and title (a test enforces the 1:1 mapping). A new code is added to both, never renamed or reused.
- **Producing errors**: use cases throw `AppException(code, detail, extensions)`; handlers may return `ApiProblems.Create(code, detail, extensions)`. Both yield the status/title from the catalogue and keep extra members (`limit`, `required`, …).
- **Everything else** goes through `AddProblemDetails` + `UseExceptionHandler` + `UseStatusCodePages` (`Api/Problems/ProblemDetailsSetup.cs`): framework status codes (unknown route, wrong method, binding failures, bare `StatusCode` results) get the default code of their status; validation problems (`HttpValidationProblemDetails`, built-in minimal API validation via `AddValidation`) get `validation_failed`; unhandled exceptions become `internal_error` without exception details. Every problem gets `type` = `https://scalenderplus.app/problems/<code-with-dashes>`, `instance` = request path and `traceId`.
- Exceptions: the system endpoints `/health/*` keep their own JSON body (also for 503), so probes stay simple.
- **OpenAPI**: the document has the `ErrorCode` enum and the `ProblemDetails` schema, and every `/api/v1` operation gets a `default` `application/problem+json` response; the typed client exports `ErrorCode` and `ProblemDetails`.

## 3. Authentication

| Client | Mechanism |
|---|---|
| Web app (same-origin) | Cookie session (`__Host-scal`, HttpOnly, Secure, SameSite=Lax) issued by Identity; CSRF: unsafe methods require header `X-Requested-With: scal` (cannot be set cross-site without CORS preflight; CORS is closed). |
| Integrators, CLI | `Authorization: Bearer scal_pat_<random>` personal access tokens with scopes. |
| Native apps (later) | OAuth 2.1 authorization code + PKCE via OpenIddict; access token (15 min) + rotating refresh token. |
| iCal feeds | Secret token in URL (see ical-caldav.md). |
| CalDAV (later) | Basic auth with app passwords. |

Token scopes: `calendars:read`, `calendars:write`, `events:read`, `events:write`, `groups:read`, `groups:write`, `imports:write`, `webhooks:write`, `account:read`. Token actions are additionally limited by the user's permissions (scopes never elevate).

Unverified accounts can sign in and use their own calendars, but cannot accept email invites/pending shares, invite others, create share links or import sources (`403 email_not_verified`). Endpoints opt in with `.RequireVerifiedEmail()` (authorization policy `verified-email`, `Api/Auth/AuthPolicies.cs`), which reads the confirmation state from the database, so it applies right after confirming.

Auth endpoints (`/api/v1/auth/…`): `register`, `login` (password → may answer `{ "twoFactorRequired": true }`), `login/2fa`, `logout`, `passkeys/options` + `passkeys/login` (v1), `confirm-email`, `forgot-password`, `reset-password`, `external/{provider}` (v1), `sessions` (list/revoke, v1). The signed-in user is `GET /api/v1/me` (§4).

### 3.1 Cookie sessions (implemented, M1)

- **Identity in Infrastructure**: `AppUser : IdentityUser<Guid>` (`Infrastructure/Identity`) with the profile columns of data-model.md §2; the user store also maintains `created_at`/`updated_at`. Core only sees `UserPreferences` (locale, zone, week start); Application only sees ids and the email templates (`Accounts/AccountEmails`). The api uses `UserManager`/`SignInManager`.
- **Cookies**: `__Host-scal` (session) — `HttpOnly`, `Secure` (always; the api sees `https` through trusted forwarded headers), `SameSite=Lax`, `Path=/`, no `Domain` (local development over `http://localhost` works: browsers treat localhost as a secure context). Without "remember me" it is a browser-session cookie; with it, persistent. Either way the ticket expires after **14 days of inactivity** (sliding). Every minute the session is re-validated against the user's security stamp, so a password reset or 2FA change ends other sessions within a minute. `__Host-scal-2fa` (5 min) carries a login whose second factor is pending. The api never redirects: no session → `401 unauthenticated`, forbidden → `403` problem.
- **CSRF** (`Api/Auth/CsrfProtection.cs`): every unsafe request (not GET/HEAD/OPTIONS/TRACE) below `/api` needs `X-Requested-With: scal` (exact value), otherwise `403 csrf_header_missing` — also on anonymous endpoints (login CSRF). Browsers send such a header cross-site only after a CORS preflight, and CORS is closed (no CORS middleware, no `Access-Control-*` headers). Exempt: endpoints marked `.DisableCsrfProtection()` (e.g. signed provider webhooks) and API-token requests (`Authorization: Bearer …` without the session cookie; hook `CsrfProtection.IsTokenRequest` for the v1 token handler). The typed client sets the header on unsafe methods.
- **Secure by default**: the `/api/v1` group requires an authenticated user; public endpoints opt out explicitly (`AllowAnonymous`) and are listed with a reason in the authorization matrix.
- **Passwords**: at least 10 characters, no composition rules (NIST 800-63B), at least 4 distinct characters. **Lockout**: 5 failed attempts in a row lock the account for 15 minutes (the response stays `invalid_credentials`); a password reset lifts it.
- **No user enumeration**: `register` always answers `202` — a new address gets the confirmation email, a registered one a "you already have an account" email (no second account, no cookie); `forgot-password` always answers `202`; failed logins are always `401 invalid_credentials` (unknown emails are hashed against a dummy for equal timing). `register` does not sign in: the web app calls `login` next.
- **Email links** (queued with `IEmailOutbox` in the same transaction as the change and the audit event; sent by the worker) point to web app routes below `App__PublicBaseUrl`: `/verify-email?userId=…&token=…` → `POST /auth/confirm-email {userId, token}`; `/reset-password?userId=…&token=…` → `POST /auth/reset-password {userId, token, newPassword}`. Tokens are Data Protection tokens (base64url in links), valid 1 day; a reset token is single-use (it is bound to the security stamp). A successful reset also confirms the email (the link proved mailbox ownership) and lifts a lockout. `POST /auth/confirm-email/resend` sends a new confirmation link to the signed-in user.
- **Audit** (`audit_events`, resource `user`): `user.registered`, `user.email_confirmed`, `user.login_succeeded` (`after.method`: `password`, `totp`, `recovery_code`), `user.login_failed` (known accounts only), `user.locked_out`, `user.logged_out`, `user.password_reset_requested`, `user.password_reset`, `user.two_factor_enabled`, `user.two_factor_disabled`, `user.recovery_codes_regenerated`, `user.recovery_code_redeemed`.

### 3.2 Two-factor authentication (TOTP, implemented, M1)

- **Login**: for a 2FA account, `POST /auth/login` answers `200 { "twoFactorRequired": true, "user": null }` and sets only the pending-login cookie `__Host-scal-2fa` (5 minutes). `POST /auth/login/2fa { code | recoveryCode, rememberMe }` completes it (session cookie as above); wrong codes are `401 invalid_credentials` and count towards the lockout; without a pending login `401 unauthenticated`. "Remember this device" is not offered (every login asks for the second factor).
- **Management** (`/api/v1/me/two-factor`, signed in): `GET` → `{ enabled, recoveryCodesLeft }`; `POST /setup` → `{ sharedKey, authenticatorUri }` (new base32 secret, `otpauth://totp/sCalenderPlus:<email>?secret=…&issuer=sCalenderPlus&digits=6` for the QR code; `409` while enabled); `POST /enable { code }` → `{ recoveryCodes }` (10 codes, shown once; wrong code → `400 validation_failed`); `POST /disable { password | code }` → `204` (also clears secret and recovery codes; no-op when off); `POST /recovery-codes { password | code }` → `{ recoveryCodes }` (replaces all; `409` when 2FA is off). Disable and regenerate need the current password or a current authenticator code (`403 reauthentication_failed`) and are rate limited per IP like the auth endpoints.
- **Codes**: RFC 6238 (HMAC-SHA1, 30 s, 6 digits; ±2 steps clock tolerance, Identity's `AuthenticatorTokenProvider`). Recovery codes (`XXXXX-XXXXX`) are stored only as SHA-256 hashes (`AppUserStore`), each works once; case, spaces and the dash are ignored when redeeming.
- Changes rotate the security stamp: other sessions end within a minute, the current one gets a fresh cookie.

## 4. Resource overview

| Resource | Endpoints |
|---|---|
| **Me** | `GET/PATCH /me`, `GET/POST /me/two-factor…` (§3.2), `GET /me/entitlements` (plan, limits, usage), `DELETE /me` (MVP, 14-day grace), `GET /me/export` (v1, GDPR, async job) |
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

### Me (implemented, M1)

`GET /api/v1/me` → `{ id, email, emailVerified, displayName, locale, timeZone, weekStart, twoFactorEnabled, createdAt }` with a strong `ETag` (hash of the representation). `PATCH /api/v1/me` takes a JSON Merge Patch (`application/merge-patch+json`; plain `application/json` is accepted too) of `displayName` (1–100 chars, trimmed), `locale` (`en`, `de`), `timeZone` (IANA id validated against the bundled tzdb with NodaTime; unknown → `422 time_zone_invalid` with `errors.timeZone`) and `weekStart` (`monday` … `sunday`); other invalid values are `400 validation_failed`. Absent **and `null`** members stay unchanged (all fields are required, so none can be removed). `If-Match` (the ETag, or `*`) is required: missing → `428`, stale → `412`. Changes are audited (`user.profile_updated` with before/after).

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
