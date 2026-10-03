# iCal Feeds and CalDAV

Native calendar apps are first-class clients. **Phase 1 (MVP, M4):** read-only personalised iCalendar subscription feeds. **Phase 2 (M8):** CalDAV, read-only first, then read/write with per-event ACL enforcement.

## 1. iCal subscription feeds (MVP)

### 1.1 Feed types

| Feed | URL | Content |
|---|---|---|
| Single calendar | `/ical/v1/{token}.ics` (token scope = calendar) | Events of one calendar visible to the token owner. |
| Aggregated | `/ical/v1/{token}.ics` (scope = aggregate) | All calendars the user has not hidden, plus "Shared with me". One subscription for everything — the default we recommend in the UI. |
| Shared with me | scope = shared_with_me | Only events shared via event overrides from calendars the user cannot otherwise see. |
| Share link (anonymous) | `/ical/v1/s/{linkToken}.ics` | Calendar as seen by `anonymous` principal, capped at `read`. |

- Token = 32 random bytes, base64url (43 chars). Stored as SHA-256 hash. The token *is* the credential, so: HTTPS only, never logged — redaction applies to app logs **and** to Caddy/Traefik access logs (`/ical/` paths masked or access logging off for them), `Referrer-Policy` set, rotate/revoke in UI, `last_used_at` shown. Password reset and "sign out everywhere" offer to rotate all feed tokens. Rotation invalidates the old token immediately.
- Also offered as `webcal://` link and QR code (for phone setup) in the UI.
- Revoked or unknown token → `404` (no oracle). Token of a deleted/disabled user → `410 Gone` (clients stop polling sooner).
- Feeds are evaluated **as the token owner**: the permission engine runs per event exactly as for the API. A user removed from a group stops seeing those events at the next poll.

### 1.2 Content rules

- `VCALENDAR` with `PRODID:-//sCalenderPlus//EN`, `VERSION:2.0`, `X-WR-CALNAME`, `X-WR-TIMEZONE` (user zone), `REFRESH-INTERVAL;VALUE=DURATION:PT1H` and `X-PUBLISHED-TTL:PT1H` (hint only; Google ignores it and polls ~every 8–24 h — documented in the UI help).
- Window: events overlapping `[now − include_past_days (default 90), +∞)`; recurring series are emitted **as RRULE + exceptions** (not expanded) so the file stays small. Hard cap 5,000 VEVENTs per feed (oldest past events dropped first).
- `UID`: single-calendar feeds emit the stored UID; aggregated / shared-with-me feeds and all `free_busy` VEVENTs emit `{eventId}@scalenderplus` (stored UIDs are only unique per calendar and imported ones may carry text).
- `SEQUENCE`, `DTSTAMP` (= `updated_at`), `LAST-MODIFIED`, `STATUS`, `TRANSP`, `CATEGORIES` (v1).
- `VTIMEZONE` components generated for every TZID used.
- `VALARM`s: **not** included by default (subscribed feeds with alarms are annoying and alarms are per-user anyway); the user can enable "include my reminders" per feed token.
- `ATTENDEE`/`ORGANIZER` omitted in subscription feeds (prevents native apps from sending iTIP replies to our feed); RSVP happens via link in the description.

### 1.3 Permission projection and access labels

| Effective level | VEVENT output |
|---|---|
| `none` | Omitted. |
| `free_busy` | `SUMMARY:<busy label>` ("Busy" / "Beschäftigt"), `CLASS:CONFIDENTIAL`, only DTSTART/DTEND/RRULE/EXDATE/RECURRENCE-ID/TRANSP and an opaque UID kept; exception VEVENTs stripped identically. Transparent events omitted. No access label, no `X-SCALENDERPLUS-*`. |
| `read` | Full content + **read-only label**. |
| `edit` / `manage` | Full content + **editable label**. |

Label settings per feed token (`label_mode`):

| Mode | read-only example | editable example |
|---|---|---|
| `prefix_emoji` **(default)** | `🔒 Board meeting` | `Board meeting` (unmarked) |
| `prefix_emoji_both` | `🔒 Board meeting` | `✏️ Training` |
| `suffix_text` | `Board meeting [read-only]` | `Training [editable]` |
| `description_only` | title unchanged | title unchanged |
| `none` | no title marker, no footer | |

Defaults rationale: in most shared calendars the majority of events a member sees are read-only *or* editable depending on role; marking only read-only events keeps titles clean for owners (who can edit everything) while warning members before they try. Pro users can customise the texts.

**Description footer** (all modes except `none`), appended after a separator, localized to the token owner's locale:

```text
—
🔒 Read-only for you · View in sCalenderPlus: https://app.example.com/e/0192f…
```

or

```text
—
✏️ You can edit this event: https://app.example.com/e/0192f…/edit
```

Machine-readable: `X-SCALENDERPLUS-ACCESS:read|edit|manage` and `X-SCALENDERPLUS-EVENT-ID:<uuid>` on every non-busy VEVENT.

Note: native apps let users edit subscribed events locally only in a few cases (they're generally read-only in subscriptions); local changes are never synced back via iCal. The labels mainly tell users *where* they can edit (our app, later CalDAV).

### 1.4 Caching, ETags and performance

- **Token check first**: every request resolves and validates the token (revoked/expired/owner deleted) before ETag or cache lookup.
- **ETag** = `W/"<hash>"` of: max(`calendar_changes.seq`) over included calendars (for shared-with-me: the calendars of events shared with the user), user `acl_version`, `acl_version` of the user's groups, each calendar `acl_version`, the owner's plan state, label settings, feed format version, and the current date bucket. Computed with one cheap query **before** rendering.
- `If-None-Match` match → `304` without loading events. `Last-Modified`/`If-Modified-Since` also supported.
- Rendered feeds are cached in-process (`IMemoryCache`, keyed by token hash + ETag, 10 min, size-limited) to absorb burst polling. Optional distributed output cache later.
- `Cache-Control: private, max-age=300`. Content-Type `text/calendar; charset=utf-8`. gzip/brotli compression enabled.
- **Revocation latency** (documented in UI): server side immediate; a client may keep showing stale data until its next poll (Apple ~15 min–1 h, Google up to 24 h). Removing access cannot recall data already synced — the UI says so when restricting an event that feeds have already delivered.
- Rate limit: 60 requests/hour per token, burst 10; excess → `429` with `Retry-After`.
- Metrics: feed requests, 304 ratio, render time, events per feed.

### 1.5 Golden-file tests

Every projection rule (busy, read, edit, labels per mode, locale, TZ, recurrence with exceptions) has a Verify snapshot test of the generated `.ics`. Additionally, CI validates output by re-parsing with Ical.Net and with a second parser (`icalendar` Python lib in a test container, or `ical.js` in the frontend test suite) to catch serializer-specific issues.

## 2. ICS import

- **MVP:** one-time upload (`.ics` file, ≤ 5 MB, ≤ 5,000 VEVENTs) into a calendar with `Lc ≥ contribute`; uploader becomes creator; recurrence/exceptions/VTIMEZONE preserved; preview with counts before commit — migration path from Google/Outlook.
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
