# Data Model

PostgreSQL 17, EF Core migrations, `snake_case` naming. All primary keys are UUIDv7 (`uuid`). All instants are `timestamptz` (UTC). Every mutable table has `created_at`, `updated_at` and an optimistic concurrency token (Postgres `xmin` mapped as row version). Soft delete only where noted (`deleted_at`). The EF Core migration history lives in `__ef_migrations_history`; migrations are applied by the `migrate` command under a Postgres advisory lock (see [coolify.md §7](../deployment/coolify.md#7-migrations)).

## 1. Entity overview

```text
users ─┬─< group_members >─ groups ─?─ organizations
       │                       │
       │         owner (user|group)
       │                       ▼
       ├────────────────── calendars ──< calendar_grants
       │                       │──< share_links
       │                       │──< import_sources ──< import_runs ──< import_candidates
       │                       ▼
       └── creator ──────── events ──< event_overrides
                               │──< event_exceptions (recurrence instances)
                               │──< event_attendees
                               │──< event_revisions
                               │──< reminders (per user)
users ──< feed_tokens, api_tokens, user_calendar_prefs, notifications, passkeys (Identity)
subscriptions, plan_limits, audit_events, calendar_changes, jobs, webhooks, webhook_deliveries
```

## 2. Identity and groups

### `users` (extends ASP.NET Identity `IdentityUser<Guid>`)

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | UUIDv7, assigned by the api |
| email, normalized_email | text | unique on normalized (`ix_users_normalized_email`) |
| user_name, normalized_user_name | text | Identity requires them; always equal to the email (login is by email) |
| display_name | text | ≤ 100 |
| locale | text | `en`, `de` |
| time_zone | text | IANA id, validated against tzdb |
| week_start | smallint | 1=Mon … 7=Sun (NodaTime `IsoDayOfWeek`) |
| acl_version | bigint | bumped on membership/role change and on overrides naming the user (permission caches) |
| created_at, updated_at | timestamptz | maintained by the user store |
| deleted_at | timestamptz null | deletion grace period |
| email_confirmed, password_hash, security_stamp, concurrency_stamp, lockout_end, lockout_enabled, access_failed_count, two_factor_enabled, phone_number, phone_number_confirmed | | ASP.NET Core Identity columns; `concurrency_stamp` changes on every update |

Identity tables `user_claims`, `user_logins`, `user_tokens` (TOTP authenticator key; recovery codes as SHA-256 hashes, never in clear text) — `IdentityUserContext<AppUser, Guid>`, no Identity roles (roles are per group). `user_passkeys` follows with passkeys (v1).

### `organizations` (v1, Team plan)

`id, name, slug unique, billing_subject_id, created_by`.
`organization_members(org_id, user_id, role [owner|admin|member], PK(org_id,user_id))`.

### `groups`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| name, description | text | name ≤ 100, description ≤ 1000 (null = none) |
| organization_id | uuid null | Team plan (FK added with `organizations`) |
| owner_user_id | uuid FK → users (restrict) | **billing owner**: one of the role-owners; plan of this user governs when no org |
| acl_version | bigint | bumped on grants/overrides naming the group |
| frozen_at | timestamptz null | over plan limit: no invites/role changes |
| member_list_visibility | smallint | 0 all members (default), 1 members and above (hidden from viewers) |
| created_at, updated_at, xmin | | `xmin` = concurrency token |

Groups are **hard-deleted** (owners only): `group_members` and `group_invites` cascade. Group-owned calendars (M2) reference `owner_group_id` with `ON DELETE RESTRICT`, so a group that still owns calendars cannot be deleted until they are transferred or deleted (the api answers `409`).

### `group_members`

`group_id FK (cascade), user_id FK → users (cascade), role smallint (0 viewer,1 member,2 admin,3 owner), joined_at, updated_at, xmin` — PK `(group_id, user_id)`, index `(user_id)`. Every membership change bumps the member's `users.acl_version`.

### `group_invites`

`id, group_id, email null, token_hash bytea unique, role, max_uses, uses, expires_at, created_by`. Email null = invite link (role ≤ member). Email invites (and pending event shares) bind only to an account whose **verified** email matches.

## 3. Calendars and grants

### `calendars`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| owner_type | smallint | 0 user, 1 group |
| owner_user_id / owner_group_id | uuid null | exactly one set (CHECK) |
| name, description, color | text | |
| default_time_zone | text | IANA |
| creators_manage_own_events | bool | default true (permissions rule 6) |
| creators_may_share_externally | bool | default false (permissions rule 7) |
| group_role_defaults | jsonb | `{"admin":"manage","member":"contribute","viewer":"read"}` |
| acl_version | bigint | bumped on grant/override/share-link/role-default/setting/ownership/freeze change |
| frozen_at | timestamptz null | over plan limit |
| archived_at | timestamptz null | |

Index: `(owner_user_id)`, `(owner_group_id)`.

### `calendar_grants`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| calendar_id | uuid FK | |
| principal_type | smallint | 0 user, 1 group |
| principal_id | uuid | |
| min_role | smallint null | for group principals |
| level | smallint | `CalendarLevel` 1 free_busy … 5 manage (numbers differ from `EventLevel`) |

Unique `(calendar_id, principal_type, principal_id, min_role)`; index `(principal_type, principal_id)` to find "calendars shared with me / my groups".

### `share_links`

`id, calendar_id, token_hash bytea unique, level (free_busy|read), label, expires_at null, revoked_at null, created_by`. The level is a ceiling for link holders (permissions §3). No passwords: iCal clients cannot send them.

### `user_calendar_prefs`

`user_id, calendar_id, hidden bool, color_override, default_reminders jsonb, sort_order` — PK `(user_id, calendar_id)`. Personal overlay; never affects others.

## 4. Events

### `events`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| calendar_id | uuid FK | |
| uid | text | iCalendar UID, unique per calendar; `{id}@scalenderplus` for native events, preserved for imports/CalDAV. Multi-calendar feeds emit `{id}@scalenderplus` instead (the same external UID may exist in two calendars). |
| creator_user_id | uuid null | null for system/import (then import source creator acts as creator); kept as tombstone after user deletion (no FK, no floor). Preserved on series split. |
| title | text | ≤ 500 chars |
| description | text | markdown subset, ≤ 20k |
| location | text | |
| url | text | |
| status | smallint | confirmed / tentative / cancelled |
| transparency | smallint | opaque / transparent |
| all_day | bool | |
| start_local, end_local | timestamp (no tz) | wall clock for timed events |
| start_date, end_date | date | for all-day events (end exclusive) |
| time_zone | text null | IANA; null only for all-day |
| start_utc, end_utc | timestamptz | computed first occurrence instant (all-day: date at UTC−14h/UTC+14h bounds so window queries in any viewer zone find it) |
| rrule | text null | RFC 5545 RRULE value |
| rdates, exdates | timestamptz[] / date[] | |
| series_until_utc | timestamptz null | end of last occurrence; null = infinite |
| occurs_range | tstzrange | **generated**: `[start_utc, coalesce(series_until_utc, end_utc, 'infinity'))` |
| has_overrides | bool | fast path for permission engine |
| sequence | int | iCal SEQUENCE, incremented on significant change |
| category_ids | uuid[] | v1 |
| import_source_id | uuid null | |
| import_key | bytea null | dedupe key (see llm-import) |
| locally_modified_at | timestamptz null | user edited an imported event → import won't overwrite those fields |
| search | tsvector | generated from title/description/location, `simple` config + unaccent |
| deleted_at | timestamptz null | soft delete (needed for sync & restore); purged after 90 days |

Indexes:

- GiST `(calendar_id, occurs_range)` (btree_gist) → window queries per calendar.
- Unique `(calendar_id, uid)`; unique partial `(import_source_id, import_key) WHERE import_key IS NOT NULL`.
- GIN `(search)`.

### `event_exceptions` (modified/cancelled occurrences of a recurring event)

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| event_id | uuid FK | series master |
| recurrence_id_utc | timestamptz | original occurrence start (iCal RECURRENCE-ID) |
| recurrence_id_date | date null | for all-day series |
| cancelled | bool | |
| title, description, location, start_local/end_local/start_date/end_date, time_zone, status, transparency | nullable | null = inherit from master |
| start_utc, end_utc | timestamptz | |

Unique `(event_id, recurrence_id_utc)`. Exceptions inherit the series ACL (MVP).

### `event_overrides`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| event_id | uuid FK | |
| principal_type | smallint | 0 user, 1 group, 2 anonymous, 3 everyone |
| principal_id | uuid null | |
| min_role | smallint null | |
| level | smallint | `EventLevel` 0 none … 3 edit (overrides never grant `manage`) |
| created_by | uuid | |

Unique `(event_id, principal_type, principal_id, min_role)`; index `(principal_type, principal_id) WHERE principal_type IN (0,1)` to find "events shared with me".

### `event_attendees` (v1)

`id, event_id, user_id null, email, display_name, role (req/opt), partstat (needs-action/accepted/declined/tentative), rsvp_token_hash` — inviting an internal user also inserts a `user → read` override (permissions rule 7).

### `event_revisions` (v1)

`id, event_id, revision int, snapshot jsonb, actor_user_id, actor_kind (user|import|api|caldav), created_at` — unique `(event_id, revision)`. Trimmed per plan retention.

### `reminders`

`id, event_id, user_id, offset_minutes, channel (email|push), next_fire_utc` — index `(next_fire_utc)`. Recomputed when the event changes; recurring events store only the next fire time.

### `calendar_changes` (sync log)

`seq bigserial PK, calendar_id, event_id, change (upsert|delete|acl), at` — index `(calendar_id, seq)`. Powers CalDAV `sync-collection`, webhooks and incremental client sync (`/changes?since=`). Trimmed after 90 days (clients older than that do full resync).

## 5. Feeds, tokens, integrations

- `feed_tokens`: `id, user_id, scope (calendar|aggregate|shared_with_me), calendar_id null, token_hash bytea unique, label_mode, label_texts jsonb, include_past_days, include_reminders bool, last_used_at, revoked_at`.
- `api_tokens`: `id, user_id, name, token_prefix (first 8 chars, for display), token_hash bytea unique, scopes text[], expires_at, last_used_at, revoked_at`.
- `webhooks`: `id, owner_subject, calendar_ids uuid[], url, secret (encrypted), events text[], disabled_at, failure_count`.
- `webhook_deliveries`: `id, webhook_id, payload jsonb, status, attempts, next_attempt_at, response_code` (trimmed 30 days).

Tokens are 32 random bytes (base64url) and stored only as SHA-256 hashes; lookup by hash. Shown once at creation.

## 6. Import (see llm-import.md)

- `import_sources`: `id, calendar_id, created_by, kind (html_llm|ics|jsonld_auto), url, schedule_cron, time_zone, hints text, mode (review|auto), auto_threshold, enabled, last_content_hash, http_etag, http_last_modified, next_run_at, consecutive_failures, tos_attested_at`.
- `import_runs`: `id, source_id, started_at, finished_at, status, path (ics|jsonld|llm|skipped_unchanged), tokens_in, tokens_out, cost_micros, stats jsonb, error`.
- `import_candidates`: `id, run_id, source_id, import_key, payload jsonb, confidence, match_event_id null, action (create|update|cancel|duplicate?), review_state (pending|accepted|rejected|auto), reviewed_by, reviewed_at`.

## 7. Billing and entitlements

- `subscriptions`: `id, subject_type (user|org), subject_id, provider, provider_customer_id, provider_subscription_id, plan (free|pro|team), interval, seats, status, current_period_end, cancel_at`.
- `plan_limits`: `plan, key, value` (seeded from `plans.json`; overridable by config).
- `usage_counters`: `subject_id, key, period (yyyy-mm or 'current'), value` — e.g. LLM runs this month. Countable resources (calendars, overrides) are counted live with indexed queries, not counters, to avoid drift.

## 8. Audit and jobs

- `audit_events`: `id, at, actor_kind (user|anonymous|system), actor_user_id null, subject_id null (billing subject, for retention), resource_type, resource_id (text), action (dotted verb, e.g. group.member.role_changed), before jsonb null, after jsonb null, ip inet null, user_agent, correlation_id (W3C trace id of the request/job)` — index `(resource_type, resource_id, at)`, `(subject_id, at)`, `(actor_user_id, at)`. Written by use cases through `IAuditLog.Record` (explicit, semantic events rather than an EF change interceptor: actions like "role changed" carry meaning a row diff doesn't, and internal tables such as `jobs` stay out of the log); the event is staged in the same unit of work and committed by the same `SaveChangesAsync`, so it exists exactly when the mutation does. Snapshots are camelCase JSON with secrets redacted (`[Sensitive]` properties and secret-like names → `"[redacted]"`). Actor, IP, user agent and trace id come from `IActorContext` (api: the request, after trusted forwarded headers; worker: system). Monthly partitions from v1 (cheap retention trimming via `DROP PARTITION`).
- `data_protection_keys`: `id, friendly_name, xml` — ASP.NET Core Data Protection key ring (`PersistKeysToDbContext`), shared by api and worker replicas; keys are stored unencrypted (database access implies key access).
- `jobs`: `id, type, payload jsonb, run_at, attempts, max_attempts, locked_by, locked_until, last_error, dedupe_key null, created_at, dead_at null` — index `(run_at) WHERE dead_at IS NULL`; `dedupe_key` unique `WHERE dedupe_key IS NOT NULL AND dead_at IS NULL` (dead jobs don't block re-enqueueing). Claim: one `UPDATE … FROM (SELECT id … WHERE dead_at IS NULL AND run_at <= now AND (locked_until IS NULL OR locked_until <= now) ORDER BY run_at LIMIT 1 FOR UPDATE SKIP LOCKED)` that sets the lease (`locked_by`, `locked_until`) and increments `attempts`. Succeeded jobs are deleted; failed ones get `run_at` = now + exponential backoff (base · 2^(attempt−1), ±20 % jitter, capped); after `max_attempts` (or on a permanent failure) `dead_at` is set and the row stays as dead letter. Every update after the claim is conditional on `locked_by` + `attempts`, so a worker whose lease expired cannot overwrite the new owner's state. See `Infrastructure/Jobs/`.

## 9. Recurrence

- The **series master** stores `RRULE`, `RDATE`, `EXDATE` and the first occurrence (wall clock + zone). Modified/cancelled instances live in `event_exceptions`, keyed by RECURRENCE-ID — a 1:1 mapping to iCalendar, so feeds and CalDAV round-trip losslessly.
- **No materialised occurrences.** Occurrences are expanded on read for the requested window (Ical.Net evaluator wrapped in `Core.Recurrence`, using NodaTime for zone math). `occurs_range` lets the index pre-select candidate series; a hard cap (e.g. 2,000 occurrences per series per request, max window 13 months) prevents abuse.
- Edit modes:
  - *this occurrence* → upsert exception;
  - *this and following* → split: set master `UNTIL` before the occurrence, create a new series (new UID, `RELATED-TO` the old one), move future exceptions;
  - *all* → update master; exceptions keep overridden fields; if start time changes, exceptions are re-keyed by the time delta.
- Infinite series: `series_until_utc = null`; `COUNT`-based rules get `series_until_utc` computed on save.
- Re-evaluation if tzdb changes (`tzdb.recompute` job).

## 10. Time zone handling

| Case | Stored as | Rendered as |
|---|---|---|
| Timed event | `start_local` + `time_zone` (IANA) + derived `start_utc` | In viewer's zone (user setting or device), with original zone shown when different. |
| All-day event | `start_date`/`end_date` (exclusive) — no zone | Same calendar date everywhere ("floating date"). |
| Recurring timed event | Wall clock + zone → DST-stable ("every Monday 18:00 Berlin" stays 18:00 local across DST). | |
| Imported floating time (no TZID) | Interpreted in the import source's/calendar's default zone; flagged in the candidate. | |
| Nonexistent local time (DST gap) | Shifted forward (NodaTime `Resolvers.LenientResolver`), user warned in UI. | |
| Ambiguous local time (DST overlap) | Earlier offset. | |

Rules:

- The UTC instant is **derived**, the wall clock + zone is **authoritative** (zone rules change; intent does not).
- The API accepts and returns `{"dateTime":"2026-11-02T18:00:00","timeZone":"Europe/Berlin"}` for timed and `{"date":"2026-11-02"}` for all-day values, plus read-only `utc` for convenience.
- iCal output uses `TZID` with generated `VTIMEZONE` components (Ical.Net) for every zone used in the feed.
- tzdb version is tracked; NodaTime tzdb is updated with dependency updates.
