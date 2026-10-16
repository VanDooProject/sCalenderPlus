# sCalenderPlus Documentation

sCalenderPlus is a shared calendar SaaS whose core idea is **different permissions for different events in the same calendar**, usable from any native calendar app via personalised iCal feeds (CalDAV later), with groups and roles, scheduled LLM-assisted imports from event websites, and Free / Pro / Team plans.

> Status: specification phase. These documents are the source of truth for the implementation; change them in the same PR as the behaviour they describe.

## Product

| Document | Contents |
|---|---|
| [screenshots.md](screenshots.md) | Screenshot gallery of the app (calendar views, sharing and permissions, groups, account, mobile, dark mode, German) |
| [product/vision.md](product/vision.md) | Vision, personas, positioning vs. Google Calendar / Outlook / Teamup / Calendly, principles, success metrics |
| [product/features.md](product/features.md) | Full feature list by area with priority (MVP / v1 / later) and rationale |
| [product/plans.md](product/plans.md) | Plan tiers, reverse trial, concrete limits, per-edition defaults, upgrade triggers, downgrade rules, pricing, self-host stance |
| [product/editions.md](product/editions.md) | Standard and poly editions, tenant model, build-time separation and leak check, runtime branding, poly use cases, discretion, adult add-on, GDPR Art. 9 |
| [product/event-directory.md](product/event-directory.md) | Poly module: public community event calendars, organiser verification and claiming, moderation |

## Architecture

| Document | Contents |
|---|---|
| [architecture/overview.md](architecture/overview.md) | Components, monorepo layout, .NET solution structure, key libraries and why |
| [architecture/permissions.md](architecture/permissions.md) | **The core**: principals, levels, resolution algorithm, worked examples, iCal mapping |
| [architecture/data-model.md](architecture/data-model.md) | Tables, keys, indexes, recurrence storage, time zone handling |
| [architecture/ical-caldav.md](architecture/ical-caldav.md) | Personalised iCal feeds, access labels, caching; CalDAV plan with per-event ACL enforcement |
| [architecture/sync.md](architecture/sync.md) | Client matrix, honest real-time story, Google Calendar API / Microsoft Graph sync and import |
| [architecture/privacy-levels.md](architecture/privacy-levels.md) | Privacy levels C/B/A (end-to-end encryption), key management, cover titles |
| [architecture/llm-import.md](architecture/llm-import.md) | Scheduled import pipeline, LLM extraction, dedupe, review queue, cost and safety controls |
| [architecture/api.md](architecture/api.md) | REST v1 conventions, auth, RFC 9457 errors, resources, OpenAPI and typed client generation |

## Development and operations

| Document | Contents |
|---|---|
| [development/workflow.md](development/workflow.md) | Conventional commits, branch naming, PR process, release-please, CI stages, testing strategy |
| [deployment/coolify.md](deployment/coolify.md) | Coolify deployment: services, env vars, health checks, migrations, backups |
| [roadmap.md](roadmap.md) | Milestones M0–M9 and the editions track ME with issue-sized work items, open questions |
| [reviews/2026-10-spec-review.md](reviews/2026-10-spec-review.md) | Spec review findings and resolutions |
| [reviews/2026-10-m0-review.md](reviews/2026-10-m0-review.md) | M0 code review (scaffolding, CI, Docker, release, e2e) findings and resolutions |
| [reviews/2026-10-m1-review.md](reviews/2026-10-m1-review.md) | M1 security review (auth, users, groups) findings and resolutions |
| [reviews/2026-10-permission-engine-review.md](reviews/2026-10-permission-engine-review.md) | Permission engine review: real-world scenarios, escalation findings, spec decisions, hot-path performance |
| [reviews/2026-10-m2-review.md](reviews/2026-10-m2-review.md) | M2 review (calendars, grants, events, overrides, recurrence): leak/authorization/recurrence findings, change-log and `acl_version` checklist |

## Key decisions at a glance

- **Permissions**: ordered levels `none < free_busy < read < (contribute) < edit < manage < owner`; calendar grants are additive; event overrides replace the calendar-derived level, most specific principal tier wins (user > group > anonymous > everyone), ties take the max; `user`/`group` overrides may restrict or elevate (max `edit`), `everyone`/`anonymous` overrides only restrict; `manage` on an event comes only from floors (calendar managers, creators); sharing with people outside the calendar's audience needs calendar `manage`; anonymous access is capped at the share link's level (≤ `read`).
- **Stack**: .NET 10 (ASP.NET Core Minimal APIs, EF Core + Npgsql, NodaTime, Ical.Net, Identity with passkeys), PostgreSQL 17 (also the job queue), Vue 3 + Vite + TypeScript (TanStack Query, FullCalendar, Tailwind + Reka UI), Playwright, GitHub Actions, release-please, Coolify.
- **Plans**: Free (forever, power features limited — e.g. 10 active events with overrides, 1 import source weekly with review), Pro €12/month, Team €8/seat/month with free viewers; billing via pluggable `IBillingProvider` (Stripe or none).
- **Reverse trial**: every new account gets Pro limits for 90 days (`Plans__TrialDays`, billing on only), then falls back to Free under the graceful downgrade rules; `GET /me/entitlements` reports plan, `trialEndsAt` and limits.
- **Editions**: one codebase, two editions (`standard`, white-labelable; `poly`), each a fully independent deployment with its own database; edition chosen at build time (`VITE_EDITION`, CI leak check), branding at runtime (`PUBLIC_BRAND_*`, `Brand__*`).
- **Privacy**: per-calendar privacy levels C (server sees everything, built) → B (content end-to-end encrypted) → A (everything encrypted), raise-only; cover titles show `free_busy` viewers a chosen text instead of "Busy".
- **Sync**: feeds are polling; real-time only in our own clients and via Google/Microsoft API write-through (later); we only control data whose source is us.
