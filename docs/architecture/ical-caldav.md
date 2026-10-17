# iCal Feeds and CalDAV

Native calendar apps are first-class clients. **Phase 1 (MVP, M4):** read-only personalised iCalendar subscription feeds. **Phase 2 (M8):** CalDAV, read-only first, then read/write with per-event ACL enforcement.

Which clients support what, real-time updates and Google/Microsoft API sync: [sync.md](sync.md).

## 1. iCal subscription feeds (MVP)

Status: **built** (M4) — `Application/Feeds` (`FeedService`, `FeedRenderer`, `VTimeZoneBuilder`, `FeedTokenService`), share links in `Application/Calendars/ShareLinkService`, endpoints in `Api/Feeds` and `Api/Calendars/ShareLinkEndpoints`; management API in [api.md "Feeds"](api.md#feeds-implemented-m4). The web UI for feed links (list, webcal link, QR code) follows separately.

### 1.1 Feed types

| Feed | URL | Content |
|---|---|---|
| Single calendar | `/ical/v1/{token}.ics` (token scope = `calendar`) | Events of one calendar as the token owner sees them (the engine decides per event, so events shared with the owner through overrides of that calendar appear even if their calendar level dropped to `none`). |
| Aggregated | `/ical/v1/{token}.ics` (scope = `aggregate`) | Every calendar the user sees and has not hidden (`user_calendar_prefs.hidden`), plus "Shared with me". One subscription for everything — the default we recommend in the UI. |
| Shared with me | scope = `shared_with_me` | Only events shared via `user`/`group` overrides from calendars the user cannot otherwise see (rule 7). |
| Share link (anonymous) | `/ical/v1/s/{linkToken}.ics` | The calendar as the `anonymous` principal with the link's level (`free_busy` or `read`) as ceiling (permissions.md §3): `anonymous`/`everyone` overrides only restrict, so `everyone → read` on a `free_busy` link stays busy and `anonymous → none` hides the event. |

- Token = 32 random bytes, base64url (43 chars), generated with `RandomNumberGenerator`. Stored as SHA-256 hash (`feed_tokens.token_hash`, `share_links.token_hash`, unique); the URL is returned **once**, by create and rotate, never by lists or reads. The token *is* the credential, so: HTTPS only, never logged — redaction applies to app logs **and** to the proxy's access logs: the api masks everything after `/ical/` in log messages, structured properties, request scopes and trace spans (`Infrastructure/Hosting/SecretRedaction`, a decorator of the logger factory, so every provider is covered), and Caddy masks the request URI and Referer the same way (`deploy/caddy/web.Caddyfile`, checked by `deploy/caddy/test-log-redaction.sh` in CI). `Referrer-Policy: same-origin` is set; rotate/revoke in UI, `last_used_at` shown (written at most every 15 minutes, not on every poll).
- **Rotation** replaces the secret, keeps the settings, and the old URL answers 404 immediately. `POST /feed-tokens/rotate-all` rotates all of a user's links and returns every new URL once. A **password reset** may rotate them all too (`rotateFeedTokens: true` in `POST /auth/reset-password`, opt-in, offered by the reset page): the request has no session, so the new secrets are not shown — the links keep their settings, are marked `needsNewLink` and get a new URL when the user rotates them. ("Sign out everywhere" will offer the same with sessions, v1.)
- Also offered as `webcal://` link (`webcalUrl` in the create/rotate response) and QR code (for phone setup) in the UI.
- Revoked, unknown, malformed or expired token (and a link whose calendar is gone) → `404` (no oracle). Token of a user in deletion grace (`users.deleted_at`) → `410 Gone` (clients stop polling sooner). Both are short `text/plain` bodies, not problem details: calendar apps cannot use them, and a problem's `instance` would echo the secret path.
- Feeds are evaluated **as the token owner**: the permission engine runs per event exactly as for the API (`EventQueryService.FeedAsync`, the same choke point). A user removed from a group stops seeing those events at the next poll.
- Limits: 50 active feed links per user and 20 active share links per calendar (`Feeds__MaxTokensPerUser`, `Feeds__MaxShareLinksPerCalendar`; beyond → `409 conflict`).

### 1.2 Content rules

- `VCALENDAR` with `PRODID:-//sCalenderPlus//EN`, `VERSION:2.0`, `CALSCALE:GREGORIAN`, `METHOD:PUBLISH`, `X-WR-CALNAME` (single-calendar and share-link feeds: the calendar name — the product name once the token owner no longer sees the calendar, whose events shared with them still come; aggregate: `Brand__ProductName`; shared with me: "Shared with me" / "Mit mir geteilt"), `X-WR-TIMEZONE` (the user's zone; share links: the calendar's default zone), `REFRESH-INTERVAL;VALUE=DURATION:PT1H` and `X-PUBLISHED-TTL:PT1H` (hint only; Google ignores it and polls ~every 8–24 h — documented in the UI help). `PRODID` is a technical identifier and stays unbranded in every edition, like the UID domain ([editions.md §3.3](../product/editions.md#33-identifiers-that-stay-unbranded)); user-visible texts (calendar name of the aggregate feed, footer) use `Brand__ProductName`.
- Window: events overlapping `[start of today − include_past_days (default 90, 0–365) in the user's zone, +∞)`; recurring series are emitted **as RRULE + RDATE + EXDATE + one `RECURRENCE-ID` VEVENT per modified occurrence** (not expanded) so the file stays small; cancelled occurrences are EXDATEs. Hard cap 5,000 VEVENTs per feed (`Feeds__MaxEvents`; exceptions count), the events ending latest kept — oldest past events dropped first.
- `UID`: single-calendar and share-link feeds emit the stored UID; aggregated / shared-with-me feeds and all `free_busy` VEVENTs emit `{eventId}@scalenderplus` (stored UIDs are only unique per calendar and imported ones may carry text).
- `SEQUENCE`, `DTSTAMP` and `LAST-MODIFIED` (= `updated_at`; exceptions: their own), `CREATED`, `STATUS`, `TRANSP`, `URL`, `CATEGORIES`, `RELATED-TO` (split series), times as `TZID` wall clock (all-day: `VALUE=DATE`, end exclusive).
- `VTIMEZONE` components generated for every TZID used (`VTimeZoneBuilder`, from the bundled NodaTime tzdb): one observance per transition from the zone interval of the earliest emitted time up to last year, then — if the zone has the same two yearly transitions for four years — yearly `RRULE` observances (`FREQ=YEARLY;BYMONTH=…;BYDAY=…`, valid for infinite series), otherwise the explicit transitions of the next twelve years; zones without transitions get one `STANDARD`. No `RDATE` lists: ical.js reads only their first value and ignores `DTSTART` next to them. Most clients use their own database for IANA ids anyway; the component must still agree with ours (tested, §1.5).
- `VALARM`s: **not** included (subscribed feeds with alarms are annoying and alarms are per-user anyway). "Include my reminders" per feed token follows with reminders (v1).
- `ATTENDEE`/`ORGANIZER` omitted in subscription feeds (prevents native apps from sending iTIP replies to our feed); RSVP happens via link in the description.
- Serialization is our own small writer (`IcsWriter`: escaping, 75-octet folding without splitting UTF-8 sequences, CRLF) rather than Ical.Net's object model: deterministic output for golden files and no second date/zone model in the request path. Ical.Net is used in the tests to re-parse the output (§1.5).

### 1.3 Permission projection and access labels

| Effective level | VEVENT output |
|---|---|
| `none` | Omitted. |
| `free_busy` | `SUMMARY:<coverTitle>` if the event (or the occurrence) has a cover title ([privacy-levels.md §5](privacy-levels.md#5-cover-titles)), else `<busy label>` ("Busy" / "Beschäftigt"), `CLASS:CONFIDENTIAL`, `TRANSP:OPAQUE`, only DTSTART/DTEND/RRULE/RDATE/EXDATE/RECURRENCE-ID, an opaque UID and `DTSTAMP` = creation (edits of hidden fields leave no trace) kept. Exceptions stripped identically: only those that change what busy viewers see get a VEVENT (moved, or another cover title); transparent occurrences become EXDATEs, exceptions that only change hidden fields disappear. Transparent events omitted — a transparent series entirely, even if single occurrences are opaque (the API window shows those; the feed errs on hiding). No access label, no `X-SCALENDERPLUS-*`, no `SEQUENCE`/`STATUS`/`LAST-MODIFIED`. |
| `read` | Full content + **read-only label**. |
| `edit` / `manage` | Full content + **editable label**. |

Label settings per feed token (`label_mode`):

| Mode | read-only example | editable example |
|---|---|---|
| `prefix_emoji` **(default)** | `🔒 Board meeting` | `Board meeting` (unmarked) |
| `prefix_emoji_both` | `🔒 Board meeting` | `✏️ Training` |
| `suffix_text` | `Board meeting [read-only]` / `[schreibgeschützt]` | `Training [editable]` / `[bearbeitbar]` |
| `description_only` | title unchanged | title unchanged |
| `none` | no title marker, no footer | |

Defaults rationale: in most shared calendars the majority of events a member sees are read-only *or* editable depending on role; marking only read-only events keeps titles clean for owners (who can edit everything) while warning members before they try. Custom texts for Pro users follow with billing (a `label_texts` column then).

**Description footer** (all modes except `none`), appended after a separator, localized to the token owner's locale (en, de):

```text
—
🔒 Read-only for you · View in sCalenderPlus: https://app.example.com/e/0192f…
```

or

```text
—
✏️ You can edit this event: https://app.example.com/e/0192f…/edit
```

(`sCalenderPlus` is `Brand__ProductName`, the links start with `App__PublicBaseUrl`; the web app's `/e/{id}` routes come with the feed UI.)

Machine-readable: `X-SCALENDERPLUS-ACCESS:read|edit|manage` and `X-SCALENDERPLUS-EVENT-ID:<uuid>` on every non-busy VEVENT of personal feeds.

**Share-link feeds** carry no access labels, footer or `X-SCALENDERPLUS-*` properties (their holders have no account to edit with); texts in the link creator's locale.

Note: native apps let users edit subscribed events locally only in a few cases (they're generally read-only in subscriptions); local changes are never synced back via iCal. The labels mainly tell users *where* they can edit (our app, later CalDAV).

### 1.4 Caching, ETags and performance

- **Token check first**: every request resolves and validates the token (well-formed, known, not revoked/expired, owner not deleted) before ETag or cache lookup — a revoked token never reaches a cached render.
- **ETag** = `W/"<hash>"` (SHA-256, 16 bytes, base64url) of: max(`calendar_changes.seq`) per included calendar, each included calendar's `acl_version` and row version (name), for "Shared with me" the latest `calendar_changes.seq` of the events whose overrides name the user or their groups (index `(event_id, seq)`) and the ids of the calendars the user sees, user `acl_version`, `acl_version` of the user's groups, the token settings (scope, label mode, past days), the owner's locale, zone and plan, the window's first day (date bucket), `Brand__ProductName`, `App__PublicBaseUrl` and the feed format version (`FeedRenderer.FormatVersion`). Share-link feeds: link id and level, the calendar's state, locale, zone and date bucket. Computed by a handful of small queries that load no event rows (token, owner, memberships, visible calendars and their grants, hidden prefs, group versions, sync-log maxima).
- `If-None-Match` match (weak comparison, `*` too) → `304` without loading events (tested with an EF command interceptor). `Last-Modified` is the time the current representation was rendered on this replica; `If-Modified-Since` answers `304` only while that render is cached (it cannot see ACL changes otherwise, so it never claims more).
- Rendered feeds are cached in-process (`FeedCache`, a dedicated `MemoryCache` keyed by token hash + ETag, absolute 10 min, size-limited by body bytes, `Feeds__CacheSizeMegabytes` default 64) to absorb burst polling. Optional distributed output cache later.
- `Cache-Control: private, max-age=300`. Content-Type `text/calendar; charset=utf-8`. gzip/brotli compression by the api for `text/calendar` only (JSON responses stay uncompressed: BREACH) and by Caddy (`encode zstd gzip`).
- **Revocation latency** (documented in UI): server side immediate; a client may keep showing stale data until its next poll (Apple ~15 min–1 h, Google up to 24 h). Removing access cannot recall data already synced — the UI says so when restricting an event that feeds have already delivered.
- Rate limit: 60 requests/hour per feed URL (token bucket: burst 10, one token per minute; `RateLimiting__Feed__*`), partitioned by the SHA-256 of the path (unknown tokens get buckets too); excess → `429 rate_limited` with `Retry-After`.
- Metrics: feed requests, 304 ratio, render time, events per feed (from the ASP.NET Core and Npgsql instrumentation; dedicated feed metrics later).

### 1.5 Golden-file tests

The repo has no Verify, so golden files are committed `.ics` fixtures (`backend/tests/SCalenderPlus.Application.Tests/Feeds/Golden`, CRLF kept by `.gitattributes`), compared byte for byte by `FeedGoldenTests` (`SCAL_UPDATE_GOLDEN=1` rewrites them for review): busy in en/de (cover titles, stripped exceptions, opaque UIDs), read in en/de, edit/manage, every label mode (suffix text in en and de), multi-calendar UIDs, share links and time zones (northern and southern DST, half-hour offsets, UTC, an irregular zone, a long-running series). A sidecar `*.instants.json` lists the UTC instants of every timed VEVENT and the first occurrences of every series by the bundled tzdb.

Cross-parser validation (CI fails on unparsable or inconsistent output):

- **Ical.Net** (`CrossParserTests`): every golden file parses with the same events, times and texts; every generated `VTIMEZONE`, evaluated by Ical.Net's recurrence engine, reproduces exactly the tzdb transitions 2023–2035 for Berlin, New York, Sydney, Kolkata, Casablanca, São Paulo, Chatham and UTC. (Ical.Net 5.2 decodes an escaped backslash before `n` as a newline; the fixtures avoid that sequence, ical.js decodes it correctly.)
- **ical.js** in Vitest (`frontend/packages/api-client/src/__tests__/icalGolden.spec.ts`): parses every golden file, checks line length and the absence of `ATTENDEE`/`ORGANIZER`/`VALARM`, resolves every TZID through the file's own VTIMEZONEs (ical.js has no zone database) to the sidecar's instants, and expands every series to the same occurrences across DST. The frontend CI job also runs when the golden files change.
- End to end (`IntegrationTests/Feeds`): projection per level through HTTP, scopes, hidden calendars, Shared with me, ETag/304 without event query, ETag changes on override and event changes, revocation over a cached render, share-link ceilings, rate limit and log capture.

## 2. ICS import

- **MVP (implemented, M4):** one-time upload (`.ics` file, ≤ 5 MB, ≤ 5,000 VEVENTs) into a calendar with `Lc ≥ contribute`; uploader becomes creator; recurrence/exceptions/VTIMEZONE preserved; preview with counts before commit — migration path from Google/Outlook. As built (`Application/Import`, api.md "ICS upload"):
  - **Reading**: an in-house reader (`IcsReader`, the counterpart of `IcsWriter`; data-model.md §9 explains why not Ical.Net — the importer needs raw RRULE text for `Core/Recurrence` and raw TZIDs, and must stay bounded on hostile input): unfolding (CRLF or LF), quoted parameters, TEXT unescaping, balanced components, nesting ≤ 10, the VEVENT count checked while reading (`422 ics_too_many_events`); anything else that is not an iCalendar object is `422 ics_invalid`. Ical.Net stays a test dependency: the import tests cross-check the fixtures with it.
  - **Mapping** (`IcsImportPlanner`, pure): one item per UID — a single event or a series master with `RRULE` (the supported subset, data-model.md §9), `RDATE`/`EXDATE` (several lines, comma lists, other TZIDs, capped at 100/1,000) and its `RECURRENCE-ID` overrides as `event_exceptions` (moved times, own title/description/location/status/transparency; values equal to the series inherit; `STATUS:CANCELLED` overrides as cancelled occurrences). An override is a complete VEVENT, so a missing DESCRIPTION/LOCATION removes it for that occurrence. Overrides of occurrences the series does not have, `RANGE=THISANDFUTURE` and timed ↔ all-day switches are skipped with a warning; overrides without their series in the file become single events with a UID of their own.
  - **Time zones** (`IcsTimeZones`): IANA ids; Windows names (`W. Europe Standard Time`, CLDR mapping in NodaTime) and Windows display names (`(UTC+01:00) Amsterdam, Berlin, …`, matched by city) from Outlook/Exchange; path-prefixed ids (`/mozilla.org/…/Europe/Berlin`); `X-LIC-LOCATION` of the VTIMEZONE. The VTIMEZONE's own rules are never used (tzdb is authoritative). Floating times and unknown TZIDs are read in the file's `X-WR-TIMEZONE`, else the calendar's zone (warned). UTC times of single events are stored in that zone (same instant); UTC series keep `UTC` so their occurrences keep their instant (RFC 5545). DST gaps shift forward as for every event (warned).
  - **Limits and lenience**: values beyond the event limits are truncated (title 500, description 20,000, location 1,000, 20 categories) and invalid URLs dropped — warned, not refused; events that cannot be represented are skipped with a reason (`start_missing`, `time_invalid`, `recurrence_not_supported` — a sub-daily rule, BYHOUR, two RRULEs —, `recurrence_invalid`, `cancelled` single events).
  - **Dedupe**: by UID among the calendar's live events (a deleted event's UID is free again) and within the file; duplicates are left out, existing events are **not updated** (one-time import, not a sync). Missing or unusable UIDs (spaces, commas, > 255 characters) get a stable generated one (`ics-{sha256…}@import.scalenderplus` of the original UID or of the event's start/end/title/rule), so a re-upload of the same file creates 0 duplicates.
  - **Privacy**: imports never set permission overrides (the calendar's defaults apply, like llm-import.md §9); `CLASS:PRIVATE`/`CONFIDENTIAL` events are imported but flagged (`private_event`) in the preview so the uploader can restrict them afterwards. No plan limit applies (plans count calendars, groups, members and overrides, not events). Verified email required (features.md: imports).
  - **Commit**: the same file is sent again with `dryRun=false`; in one transaction under the calendar's advisory lock (the UID check of event creation) the UIDs are checked again and the events inserted through `EventWriter` (one `calendar_changes` upsert each). Audit: one `calendar.ics_imported` record on the calendar with the totals (not one per event).
  - Fixtures: `backend/tests/SCalenderPlus.Application.Tests/Import/Fixtures/` (a Google Calendar export with a weekly series across DST, EXDATEs, a moved, a renamed and a cancelled occurrence, all-day single/multi-day/yearly events with an override, a UTC event, another zone, a COUNT series marked private, a cancelled event, an untitled one; an Outlook export with Windows zone names).
- **v1:**
- ICS subscription import: external URL polled by `ics.sync` job; events keep their source UID; same dedupe/update logic as LLM import but without LLM (see llm-import.md). Imported events are owned by the calendar; local edits set `locally_modified_at`.

## 3. CalDAV (later, M8)

### 3.1 Scope

Implement a **minimal, interoperable subset** of RFC 4791/6578/3744 ourselves inside the API (`/dav/`). No mature, maintained .NET CalDAV server library exists; the subset is well understood and our storage already mirrors iCalendar.

| Feature | Phase 2a (read-only) | Phase 2b (read/write) |
|---|---|---|
| Discovery: `/.well-known/caldav`, `current-user-principal`, `calendar-home-set` | ✓ | ✓ |
| `PROPFIND` (calendars, props, `getctag`, `getetag`, `current-user-privilege-set`) | ✓ | ✓ |
| `REPORT calendar-query` (time-range), `calendar-multiget` | ✓ | ✓ |
| `REPORT sync-collection` (RFC 6578) via `calendar_changes` | ✓ | ✓ |
| `GET` single resource | ✓ | ✓ |
| `PUT` (create/update) with `If-Match` / `If-None-Match: *` | – | ✓ |
| `DELETE` | – | ✓ |
| `MKCALENDAR`, `ACL` method, scheduling (RFC 6638), VTODO | ✗ | ✗ |

Authentication: HTTP Basic over TLS with **app passwords** (separate per-device credentials, scoped `caldav`, revocable) — native clients don't support OAuth broadly. Passkey/2FA users must use app passwords.

### 3.2 Resource mapping

- One CalDAV collection per calendar with `Lc ≥ free_busy`, plus a "Shared with me" collection.
- One resource per event: `/dav/calendars/{userId}/{calendarId}/{eventId}.ics` containing master + exceptions.
- Resource content = **the same projection as feeds** (busy-stripping, labels optional per app password, default `description_only` because CalDAV clients expose write ability natively).
- `getetag` = hash(event row version, effective level, label settings) → an ACL change produces a new ETag and clients re-fetch.

### 3.3 Per-event permission enforcement

| Request | Rule | Response |
|---|---|---|
| Listing/REPORT | Events with level `none` are never listed. | – |
| `current-user-privilege-set` on calendar | `read` (+ `bind` if `Lc ≥ contribute`, + `write-content` if `Lc ≥ edit`). | |
| `current-user-privilege-set` on event resource | `read` always; `write-content` + `unbind` if `Le ≥ edit`. Clients that honour it (Apple, DAVx⁵, Thunderbird partly) show the event as read-only. | |
| `PUT` new resource | Requires `Lc ≥ contribute`. Creator becomes the user. | `201` / `403` |
| `PUT` existing, `Le < edit` | Reject. | `403` + `DAV:need-privileges` body |
| `PUT` existing on `free_busy` event | Reject even if client sends unchanged busy data. | `403` |
| `PUT` changing ACL-related X-properties | Ignored (ACLs are only changed via API/UI). | |
| `PUT` with stale `If-Match` | Reject. | `412` |
| `DELETE` with `Le < edit` | Reject. | `403` |
| Moving an event between collections | Requires `manage` on event and `contribute` on target. | |
| Event becomes `none` for the user | Appears as deleted in next `sync-collection`. | |

**Client-behaviour caveat:** some clients (notably Google-backed or older Outlook connectors) ignore privileges and let the user edit locally; our `403` causes them to revert on the next sync or to show a sync error. Mitigation: label footer explains, documentation per client, and monitoring of 403 rates per user agent.

Plan gate: CalDAV read-only on Free; write requires Pro/Team (checked on `PUT`/`DELETE` → `403` with explanatory `DAV:error` + problem text).

### 3.4 Testing

- Protocol tests with recorded request/response fixtures from Apple Calendar (macOS/iOS), Thunderbird, DAVx⁵.
- `caldav-tester`-style smoke suite in CI against the full-stack docker compose.
