# Feature List

Priorities:

- **MVP** — required for the first public beta. Without it the core promise (per-event permissions, usable in native apps) does not hold.
- **v1** — required for paid launch (billing, the features people pay for, hardening).
- **later** — valuable, scheduled after v1 based on demand.

Items marked ★ are proactive additions not in the original brief, with the reason they matter.

## 1. Accounts and authentication

| Feature | Prio | Rationale |
|---|---|---|
| Email + password sign-up, email verification, password reset | MVP | Baseline. ASP.NET Core Identity. Verified email required for invites, pending shares, share links, imports (prevents hijacking pending invites by registering someone else's address). |
| ★ Passkeys (WebAuthn) | v1 | Built into .NET 10 Identity; phishing-resistant. Deferred from MVP to keep M1/M3 small; TOTP covers the 2FA need for beta. |
| ★ TOTP 2FA + recovery codes | MVP | Managers control other people's data; 2FA is expected for that responsibility. |
| ★ OAuth login (Google, Microsoft, Apple) | v1 | Reduces sign-up friction; Apple required later for iOS app store if other social logins exist. |
| ★ Session management (list/revoke devices) | v1 | Needed with feeds and tokens floating around. |
| ★ Personal API tokens (scoped, expiring) | v1 | Integrators and power users; also the basis for webhook management. |
| ★ OAuth2 authorization server (auth code + PKCE) for native apps | later | Native apps need proper token flows; until then the PWA uses cookies. |
| ★ SSO (OIDC/SAML) for Team | later | Enterprise buying criterion; only with real demand. |

## 2. Users, groups, roles

| Feature | Prio | Rationale |
|---|---|---|
| User profile (name, avatar, locale, time zone, week start) | MVP | Time zone and locale drive rendering everywhere. |
| Groups with roles owner/admin/member/viewer | MVP | Requirement; see permissions §6. |
| Invite to group by email / invite link (with role, expiry, max uses) | MVP | Clubs onboard via link in a WhatsApp group. |
| ★ Pending-invite for not-yet-registered emails | MVP | Otherwise organizers can't set up before members join. |
| Transfer group ownership, leave group, delete group | MVP | Lifecycle completeness avoids support tickets. |
| ★ Organizations (workspace above groups, seats, billing) | v1 | Needed to sell Team plan cleanly. |
| Custom roles (ordered ranks) | later | Team plan; see permissions §6.3. |
| ★ Nested groups / departments | later | Larger orgs; deliberately postponed to keep resolution cheap. |

## 3. Calendars

| Feature | Prio | Rationale |
|---|---|---|
| Multiple calendars per user and per group | MVP | Requirement. |
| Calendar settings: name, color, description, default time zone | MVP | |
| Calendar grants to users/groups(minRole)/share links | MVP | Requirement. |
| Group-role defaults per calendar | MVP | Makes group calendars work with zero config. |
| `contribute` level ("add own entries only") | MVP | Most common club need; see permissions §2.2. |
| ★ "Shared with me" virtual calendar | MVP | Destination for single-event shares (permissions rule 7). |
| ★ Personal overlay: hide/show, per-user color, notification defaults | MVP | Users need control over others' calendars without changing them. |
| Share links (read / free-busy), revocable | MVP | Public fixtures for non-members. |
| ★ Public embeddable calendar widget | v1 | Clubs put it on their website; strong acquisition loop. Built on a share link. |
| ★ Calendar transfer between user and group | v1 | Organizers leave; calendars must survive (also needed by account deletion). |
| ★ Archive calendar (read-only, out of default view) | later | Season ends; hiding via personal overlay covers it until then. |
| ★ Privacy level B `content_encrypted` (title/description/location end-to-end encrypted, times clear) | later | Default for the poly edition; keeps feeds, free/busy and reminders working. Needs key management first ([privacy-levels.md](../architecture/privacy-levels.md)). |
| ★ Privacy level A `end_to_end` (everything encrypted) | later (maybe) | Removes feeds, sync, server reminders and search; build only on proven demand. |

## 4. Events

| Feature | Prio | Rationale |
|---|---|---|
| Create/edit/delete events: title, description (markdown subset), location, all-day, start/end with time zone | MVP | |
| ★ Recurring events (RFC 5545 RRULE, EXDATE, RDATE) with edit "this / this and following / all" | MVP | Without recurrence a calendar product is not credible. |
| ★ Time zone correctness (IANA tz, DST, floating all-day dates) | MVP | Bugs here destroy trust; designed in from day one (see data-model). |
| Per-event permission overrides (user/group/role/everyone/anonymous) | MVP | **The core differentiator.** |
| ★ "Why can X see this?" explainer | MVP | Makes the core model trustworthy; reduces support. |
| ★ Optimistic concurrency (ETag / If-Match) | MVP | Shared editing without lost updates. |
| ★ Categories/tags with color | v1 | Filtering in busy club calendars. |
| ★ Attendees/invitations with RSVP (internal users) | v1 | Expected for team use; inviting someone who can't read the event creates a read override → needs override rights (permissions §4.4). |
| ★ iTIP email invitations for external attendees | later | Spam/abuse vector (calendar-invite spam); needs reputation controls. |
| ★ Reminders (per-user, email + web push) | v1 | Retention driver; per-user so they don't need edit rights; only for events with `Le ≥ read`. |
| ★ Conflict detection (warn on overlap in selected calendars) | later (M8) | Cheap, high perceived value for teams. |
| ★ Event history view (from audit log) | v1 | Shared editing needs "who changed what"; retention is a plan gate. |
| ★ Event restore (revisions) | later (M8) | Undo; Pro gate once shipped. |
| ★ Attachments (files) | later | Storage costs; links cover most needs initially. |
| ★ Event comments | later | Nice for clubs, but not core. |
| ★ Per-occurrence permission overrides | later | MVP: exceptions inherit series ACL (keeps engine simple). |
| ★ Cover titles (`coverTitle` shown to `free_busy` viewers instead of "Busy") | MVP (built) | "Busy with Alex" is more useful than "Busy" and still reveals nothing private; built in API, web app and iCal feeds ([privacy-levels.md §5](../architecture/privacy-levels.md#5-cover-titles)). |

## 5. Views and UX

| Feature | Prio | Rationale |
|---|---|---|
| Month / week / day / agenda (list) views | MVP | |
| Responsive mobile-first layout, installable PWA | MVP | Cross-platform first. |
| ★ Access indicators in UI (lock, pencil, busy, eye-off) | MVP | Users must see why they can't edit. |
| ★ Keyboard shortcuts, quick-add | v1 | Power users. |
| ★ Onboarding checklist (first calendar, invite a group, subscribe a feed; optional: an event with a cover title) | v1 | New users reach the "shared and in my calendar app" moment in under two minutes. |
| ★ Search (title, description, location; within permissions) | v1 | Grows essential with imports; Postgres full-text search. |
| ★ Free/busy availability view across members/groups | later (M8) | "When does the team have time?" — natural next step of free/busy level. |
| ★ i18n (English, German at launch) | MVP | Primary market DACH; adding later is costly. |
| ★ Accessibility (WCAG 2.2 AA) | MVP | Legal relevance in EU (European Accessibility Act); calendar grids are notoriously inaccessible. |
| ★ Dark mode / system theme | MVP | Cheap with design tokens from the start. |
| ★ Offline read cache (PWA) | later (M8) | Check today's schedule without network. |
| ★ Real-time updates in our own clients (SSE/websocket on the change log) | later (M8) | Shared editing feels live; builds on the incremental changes endpoint (M8). Feeds stay polling ([sync.md](../architecture/sync.md)). |

**Onboarding checklist.** Shown above every page of the signed-in app for an account that is not set up yet: create a calendar (there is no default calendar — registration creates none; the name is prefilled with the display name), invite a group (create one and an invite link with its QR code; *constellation* in the poly edition), subscribe a feed (the aggregate feed link, with the calendar-apps help), and optionally add an event with a cover title or its own permissions. Each tick is derived from the account's real state through the existing endpoints (a calendar at `contribute` or above; a group with another member or a pending invite; a feed link; an own event in the coming year with a cover title or overrides), so doing a step elsewhere ticks it too — nothing is stored server-side (there is no UI-preferences endpoint). Hiding it is remembered per user in the browser (`localStorage`); an account that is already set up when the checklist first loads never sees it. "Getting started" in the account menu reopens it. The mocked e2e measures the flow: 9 interactions for the three required steps, 13 with the optional event.

## 6. Native app integration

| Feature | Prio | Rationale |
|---|---|---|
| Personalised iCal feed per calendar and aggregated feed, secret token URL | MVP | Requirement; works with every native app. |
| Access labels in feeds (title prefix/suffix, description footer, X-property) | MVP | Requirement. |
| Feed token rotation/revocation, per-feed label settings | MVP | Tokens leak (shared screenshots, forwarded URLs). |
| ★ ICS import (one-time upload) | MVP | Migration from Google/Outlook is the first thing beta users try; reuses recurrence storage. |
| ★ ICS subscription import (external URL) | v1 | Many sites already provide ICS. |
| CalDAV read-only | later (M8) | Better sync than iCal polling. |
| CalDAV read/write with per-event ACL enforcement | later (M8) | Two-way sync from native apps; Pro gate. |
| ★ Native iOS/Android apps on the public API | later | After API stabilises; PWA covers until then. |
| ★ Google Calendar API / Microsoft Graph write-through sync | later | The only way to get near-real-time updates into Google and Outlook ([sync.md §3](../architecture/sync.md#3-google-calendar-api-and-microsoft-graph-connectors)). After v1: feeds cover the MVP promise, and each provider needs OAuth app verification and ongoing token/quota handling. |
| ★ Google / Microsoft API import (two-way) | later | Same connectors; replaces manual ICS export. ICS upload (MVP) and subscription (v1) cover migration until then. |

## 7. LLM import

| Feature | Prio | Rationale |
|---|---|---|
| Import sources: URL + schedule + target calendar + hints | v1 | Requirement. |
| ★ Deterministic fast paths: ICS link and schema.org JSON-LD before LLM | v1 | Cheaper, more accurate; many event sites already embed JSON-LD. |
| ★ Review queue (accept/edit/reject, bulk actions) | v1 | LLM output must not land unseen; mandatory on Free. |
| ★ Dedupe & update detection (stable keys + fuzzy match) | v1 | Re-running a scheduled import must not duplicate events. |
| ★ Change detection (content hash skip) | v1 | Saves cost; unchanged pages never hit the LLM. |
| ★ Removed-at-source handling | v1 | Cancelled concerts must not stay in calendars. |
| Auto-publish with confidence threshold | v1 | Pro gate. |
| ★ robots.txt, rate limiting, SSRF protection, ToS attestation | v1 | Legal and security baseline. |
| ★ Run log with diff (what was added/updated/skipped and why) | v1 | Debuggability and trust. |
| ★ Multi-page sources (follow pagination) | later | Pro feature once base works. |

## 8. Collaboration, notifications, integrations

| Feature | Prio | Rationale |
|---|---|---|
| ★ In-app notifications (invited, event changed in calendars I follow, import review pending) | v1 | Shared calendars need change awareness. |
| ★ Email digests (daily/weekly changes) | v1 | Club members don't open the app daily. |
| ★ Webhooks (event created/updated/deleted, signed HMAC, retries) | v1 | Integrators; plan gate. |
| ★ Audit log (who changed what, permissions changes) | MVP (record) / v1 (UI) | Must record from day one; can't backfill history. Retention is a plan gate. |
| ★ Slack/Teams/Matrix notifications | later | Via webhooks first. |
| ★ Booking pages / availability slots | later | Not core; possible Pro add-on. |

## 9. Billing and plans

| Feature | Prio | Rationale |
|---|---|---|
| Entitlements service (plan → limits), enforced server-side | MVP (skeleton) / v1 | Limits must exist in code early to avoid retrofitting. |
| Stripe checkout, customer portal, webhooks | v1 | |
| ★ Usage meter UI ("8 of 10 overrides used") | v1 | Transparent limits drive upgrades without frustration. |
| ★ Graceful downgrade (freeze, never delete, keep overrides enforced) | v1 | Prevents data loss and privacy leaks. |
| ★ EU VAT handling via Stripe Tax, invoices | v1 | Legal requirement for EU B2C/B2B. |
| Self-host mode (billing disabled, all features) | v1 | See plans.md. |
| ★ Reverse trial (Pro limits for `Plans__TrialDays`, default 90, from account creation) | v1 (built) | Users experience the core feature unlimited before meeting limits; only with billing on. Trial-end reminder 7 days before is planned. |
| ★ Entitlements endpoint `GET /me/entitlements` (plan, `trialEndsAt`, limits) | MVP (built) | The UI shows limits and trial end; usage counts follow in M5. |

## 10. Privacy, compliance, operations

| Feature | Prio | Rationale |
|---|---|---|
| ★ GDPR data export (JSON + ICS zip) | v1 | Legal right of access/portability (handled manually on request during beta). |
| ★ Account deletion with grace period; ownership hand-over for group calendars | MVP | Right to erasure applies from the first real user (public beta). |
| ★ Rate limiting (per IP, per token), sign-up throttling, abuse protection on feeds | MVP | Feed URLs are public endpoints; free accounts are a cost vector (LLM). |
| ★ Abuse reporting on share-link/widget views, admin takedown | v1 | Public calendars can host spam/phishing. |
| ★ Observability: structured logs, OpenTelemetry traces/metrics, health checks | MVP | Required for Coolify ops. |
| ★ Backups + restore drill | MVP | Calendar data loss is unacceptable. |
| ★ Admin console (users, plans, abuse, feature flags) | later (M8) | Operations without SQL; SQL runbook until then. |
| ★ Privacy policy, imprint, terms | MVP | Legally required in DACH before any public user. |
| ★ Status page, DPA template | v1 | EU B2B requirement. |

## 11. Editions and white-label

See [editions.md](editions.md).

| Feature | Prio | Rationale |
|---|---|---|
| ★ Runtime white-label branding (`PUBLIC_BRAND_*`, `Brand__*`) | MVP (built) | Name, logo, hue, legal links and support email per deployment without a rebuild; also serves self-hosters. |
| ★ Edition builds (`VITE_EDITION`, `@edition` alias) + CI leak check | MVP (built) | Poly code, wording and assets must never ship in standard builds (owner requirement). |
| ★ Poly wording overlay ("constellation") | MVP (poly) | Locale overlay only; no behaviour change. |
| ★ Discretion features (neutral name/icon, discreet notifications, quick-hide) | later (poly launch) | Safety for users who are not out to everyone; session revoke and feed rotation exist in the core. |
| ★ Event directory | later (poly) | Community events with organisers and moderation ([event-directory.md](event-directory.md)). |
| ★ Adult add-on (opt-in, age-verified, web-only, separate payment provider) | later (poly) | Keeps the core and store listings clean ([editions.md §7.2](editions.md#72-adult-add-on)). |
| ★ Edition-specific backend assemblies and api/worker images | later | Needed once the first poly-only backend module (event directory) exists. |

## Deliberately cut or postponed

- **Arbitrary permission matrices** — the ordered level model is the product's clarity.
- **Email/calendar suite features** (mail, tasks, meeting links) — not our battle.
- **Per-occurrence ACLs** — later; series-level covers >95% of cases.
- **Offline write sync** — needs native apps + conflict resolution; later.
