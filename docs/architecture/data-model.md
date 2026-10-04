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
| acl_version | bigint | bumped on membership/role change and on grants/overrides naming the user (permission caches) |
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

Groups are **hard-deleted** (owners only): `group_members` and `group_invites` cascade. Group-owned calendars reference `owner_group_id` with `ON DELETE RESTRICT`, so a group that still owns calendars cannot be deleted until they are transferred or deleted (the api answers `409 group_has_calendars`); `calendar_grants` and `event_overrides` naming the group are deleted with it by the use case (no FK on `principal_id`).

### `group_members`

`group_id FK (cascade), user_id FK → users (cascade), role smallint (0 viewer,1 member,2 admin,3 owner), joined_at, updated_at, xmin` — PK `(group_id, user_id)`, index `(user_id)`. Every membership change bumps the member's `users.acl_version`.

### `group_invites`

`id, group_id FK (cascade), email null, normalized_email null, token_hash bytea unique, role smallint, max_uses, uses (CHECK 0 ≤ uses ≤ max_uses), expires_at, created_by (no FK, kept as tombstone), created_at, revoked_at null, xmin` — index `(group_id)`, partial index `(normalized_email) WHERE normalized_email IS NOT NULL AND revoked_at IS NULL`. Email null = invite link (role ≤ member, several uses); email invites are single-use. Email invites (and pending event shares) bind only to an account whose **verified** email matches: confirming an address joins its pending invites. Pending = not revoked, not expired, `uses < max_uses`; revoked/used invites stay for the audit trail.

## 3. Calendars and grants

### `calendars`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| owner_user_id / owner_group_id | uuid null | exactly one set (CHECK `ck_calendars_one_owner`); FKs → `users` / `groups` with `ON DELETE RESTRICT` (owned calendars are transferred or deleted before the account or group goes) |
| name, description, color | text | name ≤ 100, description ≤ 1000 (null = none), color `#rrggbb` lowercase |
| default_time_zone | text | IANA, validated against tzdb |
| creators_manage_own_events | bool | default true (permissions rule 6) |
| creators_may_share_externally | bool | default false (permissions rule 7) |
| group_role_defaults | jsonb | `{"admin":"manage","member":"contribute","viewer":"read"}` (stored for personal calendars too, ignored there) |
| acl_version | bigint | bumped on grant/override/share-link/role-default/setting/ownership/freeze change |
| frozen_at | timestamptz null | over plan limit |
| archived_at | timestamptz null | |
| created_at, updated_at, xmin | | `xmin` = concurrency token |

Index: `(owner_user_id)`, `(owner_group_id)`. Calendars are hard-deleted (owners only); grants, events and their `calendar_changes` cascade. There is no `owner_type` column: the owner kind follows from which owner column is set.

### `calendar_grants`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| calendar_id | uuid FK → calendars (cascade) | |
| principal_type | smallint | 0 user, 1 group (CHECK) |
| principal_id | uuid | no FK (polymorphic); deleted with the group (and, later, the user) |
| min_role | smallint null | group principals only, stored (default 0 viewer); null for users (CHECK) |
| level | smallint | `CalendarLevel` 1 free_busy … 5 manage (CHECK; numbers differ from `EventLevel`) |
| created_by | uuid | no FK, kept as tombstone |
| created_at, updated_at, xmin | | `xmin` = concurrency token |

Unique `(calendar_id, principal_type, principal_id, min_role)` `NULLS NOT DISTINCT` (one grant per user); index `(principal_type, principal_id)` to find "calendars shared with me / my groups".

### `share_links`

`id, calendar_id, token_hash bytea unique, level (free_busy|read), label, expires_at null, revoked_at null, created_by`. The level is a ceiling for link holders (permissions §3). No passwords: iCal clients cannot send them.

### `user_calendar_prefs`

`user_id, calendar_id, hidden bool, color_override, default_reminders jsonb, sort_order` — PK `(user_id, calendar_id)`. Personal overlay; never affects others.

## 4. Events

### `events`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| calendar_id | uuid FK → calendars (cascade) | events go with their (hard-deleted) calendar |
| uid | text | iCalendar UID (≤ 255, RFC 5545-safe printable ASCII), unique per calendar among live events; `{id}@scalenderplus` for native events, preserved for imports/CalDAV. Multi-calendar feeds emit `{id}@scalenderplus` instead (the same external UID may exist in two calendars). |
| creator_user_id | uuid null | null for system/import (then import source creator acts as creator); kept as tombstone after user deletion (no FK, no floor). Preserved on series split. |
| title | text | ≤ 500 chars |
| description | text | markdown subset, ≤ 20k |
| location | text | ≤ 1000 |
| url | text | absolute http(s), ≤ 2000 |
| status | smallint | 0 confirmed / 1 tentative / 2 cancelled |
| transparency | smallint | 0 opaque / 1 transparent |
| color | text null | `#rrggbb`; null = the calendar's color |
| categories | text[] | iCalendar CATEGORIES (free text; `category_ids` may follow in v1) |
| all_day | bool | |
| start_local, end_local | timestamp (no tz) | wall clock for timed events |
| start_date, end_date | date | for all-day events (end exclusive) |
| time_zone | text null | IANA; null only for all-day |
| start_utc, end_utc | timestamptz | computed first occurrence instant (all-day: `start_date 00:00Z − 14h` / `end_date 00:00Z + 14h`, so window queries in any viewer zone find it); CHECK `end_utc >= start_utc` |
| rrule | text null | RFC 5545 RRULE value of a series master in canonical form (`UNTIL` bound to the series: UTC for timed, a date for all-day series) |
| rdates, exdates | timestamp[] | wall clock in `time_zone` (all-day: dates at midnight) — authoritative like `start_local`; EXDATEs are nominal occurrence starts |
| series_until_utc | timestamptz null | end of the last occurrence (moved exceptions included); null = infinite |
| series_start_utc | timestamptz null | start of a moved exception before the first occurrence (widens `occurs_range`); null otherwise |
| related_to | text null | iCalendar RELATED-TO: UID of the series this one was split from |
| occurs_range | tstzrange | **generated** (stored): single events `[start_utc, end_utc)` (zero-length events `[start_utc, start_utc]`, so they still overlap windows); series masters `[least(start_utc, series_start_utc), coalesce(series_until_utc, 'infinity')]` (inclusive: a zero-length last occurrence ends where it starts) |
| has_overrides | bool | fast path for permission engine |
| sequence | int | iCal SEQUENCE, incremented on significant change |
| category_ids | uuid[] | v1 |
| import_source_id | uuid null | added with imports (v1) |
| import_key | bytea null | dedupe key (see llm-import); added with imports |
| locally_modified_at | timestamptz null | user edited an imported event → import won't overwrite those fields; added with imports |
| search | tsvector | generated from title/description/location, `simple` config + unaccent; added with search (v1) |
| deleted_at | timestamptz null | soft delete (needed for sync & restore); purged after 90 days |
| created_at, updated_at, xmin | | `xmin` = concurrency token |

Indexes:

- GiST `(calendar_id, occurs_range) WHERE deleted_at IS NULL` (`ix_events_calendar_id_occurs_range`, extension btree_gist) → window queries per calendar (one lateral index scan per visible calendar, `EventQueryService.WindowSql`).
- Unique `(calendar_id, uid) WHERE deleted_at IS NULL` (a deleted event's UID may be reused, e.g. by CalDAV); unique partial `(import_source_id, import_key) WHERE import_key IS NOT NULL` (with imports).
- CHECK `ck_events_times`: all-day rows have dates (end > start) and no wall clock/zone, timed rows the reverse.
- GIN `(search)`.

### `event_exceptions` (modified/cancelled occurrences of a recurring event)

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| event_id | uuid FK → events (cascade) | series master |
| recurrence_id | timestamp | original (nominal) occurrence start as wall clock in the series' zone; all-day: the date at midnight (iCal RECURRENCE-ID) |
| cancelled | bool | exported as EXDATE |
| title, description, location, status, transparency | nullable | null = inherit from master; empty description/location = removed for this occurrence |
| start_local/end_local or start_date/end_date | nullable | moved occurrence (same kind and zone as the series; no `time_zone` column) |
| start_utc, end_utc | timestamptz null | derived instants of a moved occurrence (all-day: padded) |
| created_at, updated_at | | |

Unique `(event_id, recurrence_id)` as a **deferrable** constraint `uq_event_exceptions_event_id_recurrence_id` (re-keying shifts several keys in one transaction; created by migration SQL because EF Core cannot model deferrable constraints); CHECK `ck_event_exceptions_times`. Keyed by wall clock, not UTC as first planned: the wall clock is authoritative (§10), so a tzdb update never orphans an exception; the api shows the UTC form. No row version of its own: every exception change touches the master (its `xmin` guards concurrent edits). Read only through `EventQueryService` and written through `EventWriter` (architecture test). Exceptions inherit the series ACL (MVP).

### `event_overrides`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| event_id | uuid FK | |
| principal_type | smallint | 0 user, 1 group, 2 anonymous, 3 everyone |
| principal_id | uuid null | |
| min_role | smallint null | |
| level | smallint | `EventLevel` 0 none … 3 edit (overrides never grant `manage`; CHECK `level BETWEEN 0 AND 3`) |
| created_by | uuid | who set the entry at its current level (no FK, tombstone) |
| created_at | timestamptz | |

FK `event_id → events` (cascade; events are soft-deleted, their overrides stay for a restore). CHECK `ck_event_overrides_principal`: users and groups have an id (groups also `min_role`), `anonymous`/`everyone` neither. Unique `(event_id, principal_type, principal_id, min_role)` (NULLS NOT DISTINCT); index `(principal_type, principal_id) WHERE principal_type IN (0,1)` to find "events shared with me" and the entries of a removed member or deleted group. Written only by `EventOverrideService` (replace) and the permission lifecycle (revocations, group deletion); `events.has_overrides` is kept in step in the same transaction.

### `event_attendees` (v1)

`id, event_id, user_id null, email, display_name, role (req/opt), partstat (needs-action/accepted/declined/tentative), rsvp_token_hash` — inviting an internal user also inserts a `user → read` override (permissions rule 7).

### `event_revisions` (v1)

`id, event_id, revision int, snapshot jsonb, actor_user_id, actor_kind (user|import|api|caldav), created_at` — unique `(event_id, revision)`. Trimmed per plan retention.

### `reminders`

`id, event_id, user_id, offset_minutes, channel (email|push), next_fire_utc` — index `(next_fire_utc)`. Recomputed when the event changes; recurring events store only the next fire time.

### `calendar_changes` (sync log)

`seq bigint identity PK, calendar_id FK → calendars (cascade), event_id (no FK), change smallint (0 upsert, 1 delete, 2 acl), at` — index `(calendar_id, seq)`. Appended by `EventWriter` in the transaction of every event change; `acl` = the event's overrides changed: clients re-resolve it, and where it now resolves to `none` for them it counts as a delete (per-user feeds key on `users.acl_version`, bumped for the named users). Powers CalDAV `sync-collection`, webhooks and incremental client sync (`/changes?since=`). Trimmed after 90 days (clients older than that do full resync).

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
- `plan_limits` (with billing, optional): `plan, key, value`. Until then limits come from configuration (`Plans__{Plan}__{Limit}`, defaults in `PlansOptions`, see plans.md); keys are the API keys of `PlanLimit` (`owned_calendars`, `owned_groups`, `members_per_group`, `events_with_overrides`, `overrides_per_event`).
- `usage_counters`: `subject_id, key, period (yyyy-mm or 'current'), value` — e.g. LLM runs this month. Countable resources (calendars, overrides) are counted live with indexed queries, not counters, to avoid drift.

## 8. Audit and jobs

- `audit_events`: `id, at, actor_kind (user|anonymous|system), actor_user_id null, subject_id null (billing subject, for retention), resource_type, resource_id (text), action (dotted verb, e.g. group.member.role_changed), before jsonb null, after jsonb null, ip inet null, user_agent, correlation_id (W3C trace id of the request/job)` — index `(resource_type, resource_id, at)`, `(subject_id, at)`, `(actor_user_id, at)`. Written by use cases through `IAuditLog.Record` (explicit, semantic events rather than an EF change interceptor: actions like "role changed" carry meaning a row diff doesn't, and internal tables such as `jobs` stay out of the log); the event is staged in the same unit of work and committed by the same `SaveChangesAsync`, so it exists exactly when the mutation does. Snapshots are camelCase JSON with secrets redacted (`[Sensitive]` properties and secret-like names → `"[redacted]"`). Actor, IP, user agent and trace id come from `IActorContext` (api: the request, after trusted forwarded headers; worker: system). Monthly partitions from v1 (cheap retention trimming via `DROP PARTITION`).
- `data_protection_keys`: `id, friendly_name, xml` — ASP.NET Core Data Protection key ring (`PersistKeysToDbContext`), shared by api and worker replicas; keys are stored unencrypted (database access implies key access).
- `jobs`: `id, type, payload jsonb, run_at, attempts, max_attempts, locked_by, locked_until, last_error, dedupe_key null, created_at, dead_at null` — index `(run_at) WHERE dead_at IS NULL`; `dedupe_key` unique `WHERE dedupe_key IS NOT NULL AND dead_at IS NULL` (dead jobs don't block re-enqueueing). Claim: one `UPDATE … FROM (SELECT id … WHERE dead_at IS NULL AND run_at <= now AND (locked_until IS NULL OR locked_until <= now) ORDER BY run_at LIMIT 1 FOR UPDATE SKIP LOCKED)` that sets the lease (`locked_by`, `locked_until`) and increments `attempts`. Succeeded jobs are deleted; failed ones get `run_at` = now + exponential backoff (base · 2^(attempt−1), ±20 % jitter, capped); after `max_attempts` (or on a permanent failure) `dead_at` is set and the row stays as dead letter. Every update after the claim is conditional on `locked_by` + `attempts`, so a worker whose lease expired cannot overwrite the new owner's state. See `Infrastructure/Jobs/`.

## 9. Recurrence

- The **series master** stores `RRULE`, `RDATE`, `EXDATE` and the first occurrence (wall clock + zone). Modified/cancelled instances live in `event_exceptions`, keyed by RECURRENCE-ID — a 1:1 mapping to iCalendar, so feeds and CalDAV round-trip losslessly.
- **No materialised occurrences.** Occurrences are expanded on read for the requested window by `Core/Recurrence` (`RecurrenceRule`: parser/validator of the supported subset with canonical output; `RuleDates`: the per-period date generator; `RecurrenceSet`: rule + RDATE + EXDATE with COUNT/UNTIL, windows, lookups and the series end) and `Event.Occurrences` (exceptions applied). `occurs_range` lets the index pre-select candidate series.
- **Why in-house and not Ical.Net**: `Core` depends on NodaTime only (architecture test), the window query and the plan rules need expansion inside the domain, and our time model (wall clock authoritative, DST gaps shifted forward like `EventTimes`, exact durations) must hold for every occurrence. Ical.Net (5.x, netstandard) has its own date/zone types and evaluation pipeline, would have to be wrapped outside `Core`, and pulls a full iCalendar object model into every window query. The supported subset only has day-granular parts, so a rule is a sequence of dates: ≈ 300 lines, verified against every applicable example of RFC 5545 §3.8.5.3 (golden tests) and property tests (sorted, within the window, COUNT, EXDATE, split windows, fast-forward = scan, lookups). Ical.Net stays the candidate for iCalendar **serialization** (M4 feeds) and for cross-checking our output there.
- **Semantics**: DTSTART always counts as the first occurrence (RFC 5545 §3.8.5.3); `COUNT` counts rule occurrences before EXDATE removes any; an RDATE equal to a rule occurrence counts once; occurrences keep the first occurrence's local time of day (DST-stable) and its exact duration (all-day: number of days); an occurrence in a DST gap is shifted forward by the gap (its RECURRENCE-ID stays the nominal time).
- **Caps** (abuse protection): per series and window ≤ 1,000 occurrences (a 13-month window of a daily series has ≈ 400; sub-daily rules do not exist), per window response ≤ 5,000 items, `COUNT` ≤ 5,000, `INTERVAL` ≤ 1,000, ≤ 100 RDATEs, ≤ 1,000 EXDATEs, rule text ≤ 500 characters. Rules without `COUNT` jump to the window arithmetically; a rule that (almost) never matches stops after examining 1,000,000 days per expansion (and at the end of the window), so expansion cost is bounded.
- **Series end**: `series_until_utc` = end of the last occurrence (COUNT: enumerated on save; UNTIL: searched backwards from UNTIL; RDATEs and moved exceptions included), null for infinite series — the upper bound of `occurs_range` and of `PlanLimits.IsActive` (an infinite series is always active). Recomputed whenever times, recurrence or exceptions change (`Event.RefreshSeriesBounds`).
- Edit modes (api.md "Recurring events"):
  - *this occurrence* → upsert exception (cancel = exception with `cancelled`);
  - *this and following* → split: set the master's `UNTIL` before the occurrence (or reduce its `COUNT`), create a new series (new UID, `RELATED-TO` the old one, same creator, copied overrides), move later RDATEs/EXDATEs and exceptions;
  - *all* → update master; exceptions keep overridden fields; if the first occurrence's wall clock moves, exceptions (and RDATEs/EXDATEs unless a new recurrence is given) are re-keyed by that shift; exceptions that no longer match an occurrence are dropped and reported (`droppedExceptions`).
- Re-evaluation if tzdb changes (`tzdb.recompute` job; later): stored wall clocks and keys stay, derived UTC values (`start_utc`, `series_until_utc`, moved exceptions' instants) are recomputed.

## 10. Time zone handling

| Case | Stored as | Rendered as |
|---|---|---|
| Timed event | `start_local` + `time_zone` (IANA) + derived `start_utc` | In viewer's zone (user setting or device), with original zone shown when different. |
| All-day event | `start_date`/`end_date` (exclusive) — no zone | Same calendar date everywhere ("floating date"). |
| Recurring timed event | Wall clock + zone → DST-stable ("every Monday 18:00 Berlin" stays 18:00 local across DST). | |
| Imported floating time (no TZID) | Interpreted in the import source's/calendar's default zone; flagged in the candidate. | |
| Nonexistent local time (DST gap) | Shifted forward by the gap (NodaTime `Resolvers.LenientResolver`); the shifted wall clock is stored; the api answers with a `time_shifted_dst_gap` warning, shown in the UI. | |
| Ambiguous local time (DST overlap) | Earlier offset; `time_ambiguous_earlier_offset` warning. | |

Rules:

- The UTC instant is **derived**, the wall clock + zone is **authoritative** (zone rules change; intent does not). Implemented in `Core/Events/EventTimes` (one zone per event: start and end share `time_zone`; seconds precision).
- The API accepts and returns `{"dateTime":"2026-11-02T18:00:00","timeZone":"Europe/Berlin"}` for timed and `{"date":"2026-11-02"}` for all-day values, plus read-only `utc` for convenience.
- iCal output uses `TZID` with generated `VTIMEZONE` components (Ical.Net) for every zone used in the feed.
- tzdb version is tracked; NodaTime tzdb is updated with dependency updates.
