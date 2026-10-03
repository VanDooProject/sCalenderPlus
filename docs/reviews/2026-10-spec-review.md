# Spec Review — October 2026

Scope: all documents under `docs/` at commit `3eb8136`. Reviewer stance: product + security + architecture. Severity: **high** = data leak, privilege escalation, legal or launch blocker; **med** = likely bug, abuse or rework; **low** = clarity/consistency.

## 1. Permission model

| # | Finding | Sev | Resolution |
|---|---|---|---|
| P1 | Any principal with effective `manage` via an override could edit overrides, so access could be chained (A gives B `manage`, B shares with C…), and a contributor could give a stranger `manage`. | high | **Fixed.** Overrides cap at `edit`; event `manage` only from floors (permissions rule 9, §4.2 step 4, §4.4). |
| P2 | A contributor (creator floor) could share club events with arbitrary outsiders through `user:`/`group:` overrides, which leaks data outside the calendar's audience. | high | **Fixed.** "External" principals need calendar `manage` unless `creatorsMayShareExternally` (default off) (rule 7, §4.4, new column). |
| P3 | `everyone → read` by a contributor raised a `free_busy` share link and `free_busy` calendar audience to full details. | high | **Fixed.** `everyone`/`anonymous` overrides are restrict-only; the link level is a ceiling (§3, §4.2, Example G). |
| P4 | Attendee invites (an `edit` capability) implicitly created `user → read` overrides, so `edit` users could share with anyone. | high | **Fixed.** Inviting someone who can't read the event needs override rights (§4.4, features, M7 AC). |
| P5 | A "this and following" split by an `edit` user could make them creator of the new series, so they gained the `manage` floor. | high | **Fixed.** Split keeps the original creator and copies overrides (§4.6, data-model, M2 AC). |
| P6 | Pending invites auto-joined on sign-up with a matching email but no verification, so registering a victim's address hijacked the invite. | high | **Fixed.** Binding requires a verified email; unverified accounts are restricted (api §3, data-model, M1 AC). |
| P7 | Moving events between calendars had no rules for overrides, UID clashes, plan counts or change logs, and no endpoint. | med | **Fixed.** §4.6 + `POST /events/{id}/move` + M2 item. |
| P8 | Removing a member left their individual `user:` overrides, so they kept seeing those events. | med | **Fixed.** Removal revokes them by default (opt-out), M2 item. |
| P9 | Deleted users: no rules for grants, overrides, the creator FK, import sources or tokens. | med | **Fixed.** §4.6; `creator_user_id` is a tombstone with no FK. |
| P10 | Feed ETag missed changes: overrides on shared-with-me events from other calendars, role-default and setting changes, freezes. Token revocation vs in-memory cache was undefined. | high | **Fixed.** Expanded `acl_version` (users, groups, calendars), the token is checked before the cache, and revocation latency is documented (permissions §8, ical §1.4). |
| P11 | `free_busy` projection leaked through preserved import UIDs, `X-SCALENDERPLUS-*`, CATEGORIES and unstripped exception VEVENTs. Full-text search over busy events would also leak titles by matching. | high | **Fixed.** Opaque UID, exception stripping, search/reminders only on `Le ≥ read` (permissions §7/§8, ical §1.3). |
| P12 | Feed tokens leak via Caddy/Traefik access logs (only app logs were redacted); there was no rotation on password reset. | med | **Fixed.** Proxy log redaction, rotate-all option (ical §1.1, coolify, M4 AC). |
| P13 | Calendar and event level numbers differ for the same names (`edit` = 4 vs 3). | low | **Fixed.** Separate enums documented (permissions §8, data-model). |
| P14 | Group admins could demote or remove each other ("admin wars"); invite links could carry `admin`. | med | **Fixed.** §6.1. |
| P15 | Aggregated feeds assumed globally unique UIDs, but `uid` is unique only per calendar (imports keep external UIDs). | med | **Fixed.** Multi-calendar feeds emit `{id}@scalenderplus`. |
| P16 | Share-link `password_hash` can't work with iCal clients and wasn't in the API or plans. | low | **Fixed.** Removed. |
| P17 | Group overrides with different `minRole` combine with max (e.g. you can't give viewers less than members via role-scoped overrides). | low | **Deferred.** Documented as intended; revisit with custom roles. |
| P18 | Data already delivered to native apps can't be recalled after restricting an event. | med | **Fixed (documented).** UI hint in permission editor; revocation latency in ical §1.4. |
| P19 | Tenant isolation relied on convention only. | med | **Fixed.** Architecture test (single query service) + cross-tenant case in authz matrix (workflow §6). |

## 2. Plans and abuse

| # | Finding | Sev | Resolution |
|---|---|---|---|
| B1 | A single €12 Pro owner could run 10 groups × 150 members with unlimited editors, which makes Team pointless for clubs/companies. | med | **Fixed (proposed number).** New "Editors" limit (Free 15 / Pro 25 / Team = seats). Owner decision (OQ 9). |
| B2 | Team seat loophole: viewers with individual `edit` grants/overrides created content without a seat. | med | **Fixed.** Seat = anyone able to create/change content via any path. |
| B3 | LLM cost abuse via sybil Free accounts (dry runs not counted, 8 runs each). | high | **Fixed.** Verified non-disposable email, per-user + per-subject dry-run caps, global daily Free LLM budget, sign-up throttling (llm-import §2/§8, M6 item). |
| B4 | Downgrade semantics incomplete: groups, share links, tokens, webhooks, labels, CalDAV write, ownership transfer; "frozen" didn't say whether deletes were allowed. | med | **Fixed.** Full table in plans.md. |
| B5 | The plan's 60 req/min API limit would throttle the web app itself (same API). | med | **Fixed.** Plan limits apply to bearer tokens; sessions get an abuse limit. |
| B6 | Free share links: only "read-only" was allowed while the more private free/busy was gated. | low | **Fixed.** |
| B7 | Multiple group owners but a single billing subject: unclear whose plan governs. | med | **Fixed.** `groups.owner_user_id` = billing owner, transferable. |
| B8 | One member can exhaust a Free group's 10-override pool. | low | **Deferred (accepted).** Per-creator meter; revisit with beta data. |
| B9 | "Restore previous version" sold in Pro but scheduled after paid launch (M8). | med | **Fixed.** Marked later; history *view* added to M5. |
| B10 | Free limit of 10 overrides may be hit quickly by the family persona's "private entries". | med | **Owner decision** (OQ 4). |

## 3. Missing / under-specified

| # | Finding | Sev | Resolution |
|---|---|---|---|
| M1 | Public beta (M4) had no prod environment, backups, account deletion or imprint/privacy policy (all in M7), so real EU personal data would be held without them. | high | **Fixed.** Moved into M4 "beta readiness". |
| M2 | No one-time ICS upload before beta, so migrating users can't bring data. | med | **Fixed.** MVP, M4 item, limits in ical §2. |
| M3 | All-day events indexed at calendar-TZ midnight can be missed by viewers in far zones. | med | **Fixed.** ±14 h index bounds, M2 AC. |
| M4 | Soft-deleted events retained forever. | low | **Fixed.** Purged after 90 days. |
| M5 | Abuse of public calendars/widgets and external iTIP invites as spam vectors. | med | **Fixed.** Abuse reporting (v1); iTIP to externals deferred to later. |
| M6 | Notifications/digests/reminders didn't state they re-check permissions at send time. | med | **Fixed.** permissions §8. |
| M7 | Feature table claimed v1 for items scheduled in M8 (availability, conflicts, offline cache, admin console) and had no roadmap items for widget, session management, calendar transfer, audit UI. | med | **Fixed.** Features re-prioritised; roadmap items added. |
| M8 | Error tracking (`PUBLIC_SENTRY_DSN`) appears only as an env example. | low | **Deferred.** Owner to choose tool; OTel covers backend. |

## 4. Over-engineering for MVP

| # | Finding | Sev | Resolution |
|---|---|---|---|
| O1 | Passkeys in MVP (UX + virtual authenticator tests) while TOTP covers 2FA. | low | **Fixed.** Moved to v1 (M7). |
| O2 | `/changes` sync endpoint and `Idempotency-Key` have no MVP client. | low | **Fixed.** Later / v1. |
| O3 | Calendar archive. | low | **Fixed.** Later (personal hide covers it). |
| O4 | Visual regression + 3 browsers on every PR is slow/flaky pre-beta. | low | **Fixed.** Chromium on PR, others nightly, visual snapshots post-beta. |
| O5 | Landing Dockerfile in M0. | low | **Fixed.** Moved to M7. |
| O6 | Team plan (orgs + seats) at paid launch is a large chunk for an uncertain segment. | med | **Owner decision** (OQ 10); split into two items. |

## 5. Consistency and roadmap

| # | Finding | Sev | Resolution |
|---|---|---|---|
| R1 | Release-please tags created with `GITHUB_TOKEN` don't trigger `release.yml`, so images were never built. | high | **Fixed.** Build in release-please workflow on `release_created` (workflow §4, M0). |
| R2 | Commit table says `feat` → patch pre-1.0 but config lacked `bump-patch-for-minor-pre-major`. | low | **Fixed.** |
| R3 | Dependency order: emails before email confirmation, problem details and authz matrix before first endpoints, DB before `/health/ready`, Dockerfiles before fullstack e2e, audit before group mutations. | med | **Fixed.** Reordered. |
| R4 | Oversized items (reminders+push+notifications+digests; tokens+webhooks; search+categories; CalDAV read-only; app shell+auth+i18n; event dialog+recurrence editor). | med | **Fixed.** Split. |
| R5 | M0 gaps: LICENSE, CODEOWNERS, PR template, commitlint config, `ValidateOnStart`, healthcheck CLI, security headers, dev compose, image hand-off to e2e, Coolify `exclude_from_hc`. | med | **Fixed.** Added to M0 items. |
| R6 | "MVP = M0–M4 + M3 essentials" was self-contradictory; M8 items lacked AC. | low | **Fixed.** |
| R7 | Coolify compose deploys recreate containers (brief downtime) despite the "rolling" wording. | low | **Fixed (documented).** |

## Left for the owner

Open questions 4 (Free override limit), 9 (Pro editors limit), 10 (Team at launch) and 11 (creator external sharing default) in [roadmap.md](../roadmap.md), plus the existing ones (name, license, pricing, LLM sub-processor, hosting).
