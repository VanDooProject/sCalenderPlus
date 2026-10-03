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

## 5. Views and UX

| Feature | Prio | Rationale |
|---|---|---|
| Month / week / day / agenda (list) views | MVP | |
| Responsive mobile-first layout, installable PWA | MVP | Cross-platform first. |
| ★ Access indicators in UI (lock, pencil, busy, eye-off) | MVP | Users must see why they can't edit. |
| ★ Keyboard shortcuts, quick-add | v1 | Power users. |
| ★ Search (title, description, location; within permissions) | v1 | Grows essential with imports; Postgres full-text search. |
| ★ Free/busy availability view across members/groups | later (M8) | "When does the team have time?" — natural next step of free/busy level. |
| ★ i18n (English, German at launch) | MVP | Primary market DACH; adding later is costly. |
| ★ Accessibility (WCAG 2.2 AA) | MVP | Legal relevance in EU (European Accessibility Act); calendar grids are notoriously inaccessible. |
| ★ Dark mode / system theme | MVP | Cheap with design tokens from the start. |
| ★ Offline read cache (PWA) | later (M8) | Check today's schedule without network. |

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

## Deliberately cut or postponed

- **Arbitrary permission matrices** — the ordered level model is the product's clarity.
- **Email/calendar suite features** (mail, tasks, meeting links) — not our battle.
- **Per-occurrence ACLs** — later; series-level covers >95% of cases.
- **Offline write sync** — needs native apps + conflict resolution; later.
