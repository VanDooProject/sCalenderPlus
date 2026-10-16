# Editions and White-Label

sCalenderPlus is built from **one codebase into two editions**:

| Edition | Key | Audience | Default brand |
|---|---|---|---|
| **Standard** | `standard` | Clubs, teams, families (see [vision.md](vision.md)); white-labelable per deployment | "sCalenderPlus" |
| **Poly** | `poly` | Customer edition for polyamorous, non-monogamous and kink-positive communities ("polycule calendar") | set by the customer (open question, [roadmap](../roadmap.md#open-questions-for-the-owner)) |

The per-event permission model is the same in both; the poly edition adds wording, defaults and modules that only make sense for its audience.

## 1. Tenant model

Each edition is a **fully independent tenant**: own deployment, own database, own domain, own billing (Stripe) account, own images. There is **no multi-tenancy inside one database** — no shared users, no cross-edition queries, no tenant column.

| | Standard | Poly |
|---|---|---|
| Coolify project | `scalenderplus` | own project (see [coolify.md §12](../deployment/coolify.md#12-a-second-independent-edition-deployment)) |
| Database | own | own |
| Web image | `scalenderplus-web` | `scalenderplus-poly-web` |
| api / worker images | `scalenderplus-api`, `scalenderplus-worker` | same images until poly-only backend modules exist (§2.3) |
| Domain, SMTP sender, Stripe account | own | own |

Rationale: the poly edition processes special-category data (§7); hard separation keeps a breach, subpoena or sub-processor of one tenant from touching the other, and lets each tenant have its own legal entity, terms and privacy policy.

## 2. Build-time edition separation

The edition is chosen at **build time** so poly-only code, wording and assets **never ship in standard builds** (owner requirement: nothing from the poly edition leaks into white-label versions).

### 2.1 Frontend

- Vite env `VITE_EDITION=standard|poly` (default `standard`).
- Edition-specific code lives in `frontend/app/src/editions/<edition>/` and is imported only through the Vite alias `@edition`, which points at the selected folder. The other folder is never imported, so it is never bundled.
- Shared code may import `@edition/…` but never `editions/poly/…` or `editions/standard/…` directly (lint rule).
- Static shell before `/config.json` loads: the build writes the edition's default name (`brand.json`) into `index.html`'s `<title>` and emits the edition's `favicon.svg` (default: `src/assets/favicon.svg`) as `/favicon.svg`. The poly favicon is a plain, discreet calendar glyph in the poly hue; the leak check matches it by content hash, so it can't end up in a standard build.

### 2.2 CI leak check

A CI step builds the **standard** edition and fails if any poly marker appears anywhere in `dist/` (`frontend/app/scripts/check-edition-leak.mjs`). The markers are derived from the other edition's folder itself (every string literal and message not shared with the base locales, the folder path, and other files such as the favicon by content hash), so new poly wording is covered without maintaining a list. As a self-test the built edition's own markers must all be found in its build, so a change in how strings are bundled cannot make the check pass blindly. The poly build is checked the same way against the standard edition.

### 2.3 Images

- The web image is built per edition with build arg `EDITION`: `scalenderplus-web` (standard) and `scalenderplus-poly-web` (poly).
- `scalenderplus-api` and `scalenderplus-worker` are shared until poly-only backend modules exist (event directory, adult add-on). Those then go into **separate assemblies** that only the edition-specific api/worker images (`scalenderplus-poly-api`, `scalenderplus-poly-worker`) reference. *Spec only; not built yet.*

## 3. Runtime branding (white-label, no rebuild)

Per deployment, without rebuilding the image. All keys are optional; unset keys fall back to the edition defaults.

### 3.1 Web (`/config.json`)

Caddy renders `brand: { name, logoUrl, hue, supportEmail, imprintUrl, privacyUrl, termsUrl }` into `/config.json` from:

| Variable | `brand.` key | Default |
|---|---|---|
| `PUBLIC_BRAND_NAME` | `name` | edition default (standard: "sCalenderPlus") |
| `PUBLIC_BRAND_LOGO_URL` | `logoUrl` | built-in logo |
| `PUBLIC_BRAND_HUE` | `hue` | `268` — OKLCH hue (0–360) of the brand palette |
| `PUBLIC_BRAND_SUPPORT_EMAIL` | `supportEmail` | none (support link hidden) |
| `PUBLIC_BRAND_IMPRINT_URL` | `imprintUrl` | none |
| `PUBLIC_BRAND_PRIVACY_URL` | `privacyUrl` | none |
| `PUBLIC_BRAND_TERMS_URL` | `termsUrl` | none |

### 3.2 Backend

| Variable | Default | Used for |
|---|---|---|
| `Brand__ProductName` | "sCalenderPlus" | Email subjects and bodies, any product name rendered server-side (later: iCal `PRODID`, feed footer) |
| `Brand__SupportEmail` | none | Support contact in emails |

### 3.3 Identifiers that stay unbranded

Branding covers what people read. Technical identifiers that clients, other calendars or operators store and compare stay the same in every edition and deployment, deliberately:

| Identifier | Value | Why it stays |
|---|---|---|
| Problem `type` URIs | `https://scalenderplus.app/problems/{code}` | Part of the error contract ([api.md §2](../architecture/api.md#2-errors--rfc-9457-problem-details)); clients may match on them. Users see the localized `title`/`detail`, never the URI. |
| iCalendar UID domain of native events | `{id}@scalenderplus` | UIDs must never change: calendar apps de-duplicate and update subscribed events by UID, so a rename would duplicate every event. |
| Data Protection application name | `scalenderplus` | Isolates the key ring and the cookies/tokens it protects; changing it invalidates every session and protected token. |
| OpenTelemetry service names | `scalenderplus-api`, `scalenderplus-worker` | Operator-facing; dashboards and alerts key on them. |
| OpenAPI document title | `sCalenderPlus API` | The document is exported at build time and committed (`backend/openapi/v1.json`), before any deployment's `Brand__*` exists; a per-deployment title would make it drift. Developer-facing only. |

None of them reaches end users in normal use. If a tenant ever needs them branded, that is a new, versioned contract (e.g. a new problem type base), not a `Brand__*` setting.

## 4. Poly wording overlay

Poly builds merge a locale JSON overlay over the base locale messages (poly build only, so it is covered by the leak check).

| Base term | Poly term (en / de) |
|---|---|
| group | constellation / Konstellation |
| roles (owner/admin/member/viewer) | same roles; softer labels where the base wording reads corporate |

Keys, API values and permission levels are unchanged — only displayed text differs.

## 5. Feature matrix per edition

Status: **built** = on the current branch; otherwise the roadmap priority.

| Feature | Standard | Poly | Status |
|---|---|---|---|
| Per-event permissions, groups, iCal feeds, imports (core) | ✓ | ✓ | per [features.md](features.md) |
| Runtime branding (`PUBLIC_BRAND_*`, `Brand__*`) | ✓ | ✓ | built |
| Edition build + leak check | ✓ | ✓ | built |
| Cover titles ([privacy-levels.md §5](../architecture/privacy-levels.md#5-cover-titles)) | ✓ | ✓ | built (API + web) |
| Reverse trial + `GET /me/entitlements` ([plans.md](plans.md)) | ✓ | ✓ | built |
| Poly wording overlay ("constellation") | – | ✓ | planned (ME) |
| More generous Free limits (`Plans__Free__*`, §9) | – | ✓ | configuration only |
| Discretion features (§6) | – | ✓ | later |
| Privacy level B (content encrypted) | ✓ | ✓ (default) | later |
| Privacy level A (end-to-end) | ✓ | ✓ | later, maybe |
| Google / Microsoft sync and import ([sync.md](../architecture/sync.md)) | ✓ | ✓ | later |
| Event directory ([event-directory.md](event-directory.md)) | – | ✓ | later |
| Adult add-on (§7.2) | – | ✓ | later |

## 6. Poly target group and use cases

Market analysis of the target group (size, willingness to pay) is the customer's responsibility, not ours.

| Use case | What the product does |
|---|---|
| **Partners** | Each person keeps their own calendar; partners get `read` on shared dates, `free_busy` on the rest. |
| **Metamours** (partners of partners) | See availability, not content: `free_busy` with a **cover title** ("Busy with Alex") instead of the real title. |
| **Constellations / overlapping groups** | One person is in several constellations; a constellation is a group, and per-event overrides decide what each one sees. |
| **Date nights** | Event shared `read` with the one partner involved, `free_busy` (with cover title) for everyone else. |
| **Overnights** | All-day or multi-day blocks visible to co-parents and nesting partners for planning, details only to the people involved. |
| **Kids' schedules** | Shared calendar with co-parents, editable by all parents; partners without parenting role get `free_busy`. |
| **Nesting partners** | Household calendar (shared flat pattern) with `edit` for both, separate from each person's dating calendar. |
| **Availability across partners** | Free/busy overlay across everyone's calendars ("when are we all free?") — builds on the availability view (M8). |

### 6.1 Discretion features (poly)

| Feature | Purpose |
|---|---|
| Neutral app name and icon option (PWA manifest) | Home screen does not reveal what the app is for. |
| Discreet notification and email texts | "You have 1 new update" instead of event titles or names on lock screens and in inboxes. |
| Quick-hide | One tap/shortcut switches to a neutral screen and locks the session. |
| Session revoke | List and revoke sessions (shared core feature, M7), prominent in poly settings. |
| Feed token rotation | Rotate all feed URLs at once (core feature, M4), prominent after a breakup or lost device. |

## 7. Legal and platform obligations (poly)

### 7.1 GDPR Art. 9

Calendar entries in the poly edition reveal data about sex life and relationships — **special-category data** under GDPR Art. 9.

| Obligation | Implementation |
|---|---|
| Explicit consent (Art. 9(2)(a)) | Separate, unticked consent at sign-up naming the data category; withdrawable (= account deletion). |
| DPIA (Art. 35) | Data protection impact assessment before launch; owner on the customer side (open question). |
| Minimal sub-processors | Hosting, database backups and SMTP only; no analytics, no error tracking with payloads, no third-party fonts or CDNs. |
| No LLM processing of private events | The LLM importer only reads public web pages (e.g. for the event directory); users' private events are never sent to an LLM provider. |
| Data minimisation | Privacy level B as default once built ([privacy-levels.md](../architecture/privacy-levels.md)); discreet notifications (§6.1). |

### 7.2 Adult add-on

The core product and its store listings stay **a clean calendar**. Explicit content (event directory images, adult listings) is an **opt-in add-on**:

- Age verification before unlocking.
- **Web only** — never unlocked in store builds (native apps, later), not mentioned on store pages or in core marketing.
- **Separate payment provider** that accepts adult businesses; Stripe stays for the core subscription.
- Built as a poly-only module (§2.3), so it is absent from standard builds.

### 7.3 App stores

- Store builds (native apps, later) are built without the adult add-on; app store review guidelines restrict sexual content, and store listings describe a calendar for relationships and communities.
- In-app digital purchases in store apps are subject to store billing rules; keeping the adult add-on web-only also keeps it out of store billing.
- The neutral name/icon option (§6.1) must still match the store listing's declared app; check current guidelines before submission.

## 8. Positioning

The standard edition remains the primary product ([vision.md](vision.md)); the poly edition is a customer edition on the same engine. Features are built in the shared core when both audiences benefit (cover titles, privacy levels, sync) and as poly modules only when they don't.

## 9. Plan defaults per edition

Both editions use the plans and reverse trial of [plans.md](plans.md). The poly deployment overrides Free limits via `Plans__Free__*` ([plans.md §Per-edition defaults](plans.md#per-edition-defaults)).
