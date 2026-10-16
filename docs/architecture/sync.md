# Sync and Real-Time

How changes reach people's devices, what we can promise per client, and what we cannot control. Feeds and CalDAV are specified in [ical-caldav.md](ical-caldav.md); this document covers the client matrix, real-time updates and API-based sync with Google and Microsoft. *Spec only; nothing here is built beyond feeds (M4).*

## 1. Client matrix

| Client | iCal feed (M4) | CalDAV (M8) | API sync (later) | Update latency |
|---|---|---|---|---|
| **Google Calendar** | ✓ subscription by URL | ✗ Google offers no CalDAV for third-party servers; Android needs DAVx⁵ | ✓ Google Calendar API write-through | feed: hours (Google decides, cannot be changed); API: seconds |
| **Apple Calendar** (macOS/iOS) | ✓ | ✓ native | – | feed: user-chosen interval; CalDAV: on push/poll, minutes |
| **Outlook** (desktop/web/mobile) | ✓ subscription by URL | ✗ not native | ✓ Microsoft Graph write-through | feed: hours; API: seconds |
| **Thunderbird** | ✓ | ✓ native | – | user-chosen interval |
| **Android via DAVx⁵** | ✓ (ICSx⁵) | ✓ | – | user-chosen interval |
| **Our clients** (web/PWA, native later) | – | – | REST API | real-time (§2) |

## 2. The honest real-time story

- **iCal feeds are polling by the client.** We set refresh hints (`REFRESH-INTERVAL`), but Google ignores them and polls every few hours; Outlook similar; Apple uses the user's interval. No server-side change can make a subscribed feed real-time.
- **Real-time exists in two places only:**
  1. **Our own clients**: server-sent events (or websocket) on the `calendar_changes` log, plus web push for reminders and notifications. The client refetches the affected window; permission filtering stays server-side.
  2. **Write-through sync** into Google and Microsoft calendars via their APIs (§3): we push changes as they happen.
- Marketing says exactly this: "Instant in the app and in connected Google/Outlook accounts; subscribed feeds update when your calendar app refreshes them."

## 3. Google Calendar API and Microsoft Graph connectors

| Aspect | Rule |
|---|---|
| Connect | User connects a Google or Microsoft account (OAuth, minimal calendar scopes); tokens stored encrypted, revocable in settings. |
| Target | We write into a **dedicated calendar** we create in that account (e.g. "sCalenderPlus"), never into the user's primary calendar. |
| Projection | Same per-user projection as feeds: `free_busy` events written busy-only with **cover title** ([privacy-levels.md §5](privacy-levels.md#5-cover-titles)), `none` never written; access lost → event deleted there. |
| Write-through | Worker job on every `calendar_changes` entry affecting the user; idempotent by event id; periodic full reconcile for drift. |
| Edits made in Google/Outlook | Ignored or reverted on reconcile (the dedicated calendar is a projection); edits happen in our app (later CalDAV). |
| Import (two-way, later) | The same connectors read the user's own Google/Outlook calendars into one of our calendars (replacing manual ICS export), with the dedupe rules of ICS subscription import. |
| Privacy levels | C: full or busy-only; B: busy-only with cover title; A: not available. |
| Plan gate | To be decided with the item (sync is a candidate Pro feature). |

## 4. What we control

**We only control data whose source is us.** Once an event is delivered to Google, Apple or Outlook (feed, CalDAV or API), its copy is under that provider's control until the next sync removes it; access revocation cannot recall it ([ical-caldav.md §1.4](ical-caldav.md#14-caching-etags-and-performance)).

Data whose source is Google, Outlook or another service is outside our control; we can only **import** it:

| Path | Milestone |
|---|---|
| ICS upload | M4 |
| ICS subscription (external URL) | v1 (M6) |
| Google / Microsoft API import | later (§3) |
