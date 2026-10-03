# Product Vision

## One-liner

**sCalenderPlus is the shared calendar where every entry can have its own permissions** — one calendar for the whole club, team or family, in which some entries are editable by everyone, some are read-only, some show only "Busy", and some are private — and it all shows up correctly in the calendar apps people already use.

## Problem

Shared calendars today are all-or-nothing per calendar:

- Google Calendar / Outlook: you share a *calendar* with "see all details", "see free/busy" or "make changes". To have one private or one locked entry you must create another calendar, and then everyone has to subscribe to N calendars.
- Teams work around it with naming conventions ("[DO NOT EDIT]"), duplicated calendars, or a spreadsheet.
- Clubs and associations publish fixtures on websites that nobody subscribes to, so members copy dates manually.

The result is calendar sprawl, accidental edits of important entries, leaked private entries, and stale data.

## Vision

1. **One calendar per real-world context**, with per-entry permissions as the exception mechanism (see [permissions](../architecture/permissions.md)).
2. **Native apps are first-class clients.** People live in Apple Calendar, Google Calendar, Outlook and Thunderbird. We meet them there via personalised iCal feeds (MVP) and CalDAV (later), and we tell them in the event itself whether they can edit it.
3. **Calendars that fill themselves.** A scheduled, LLM-assisted importer turns event websites (venues, leagues, schools, municipalities) into structured, deduplicated calendar entries — with a review queue so nothing junk lands unseen.
4. **Explainable access.** Every permission decision can be explained in one sentence ("Vic can read this because a user-level override grants read"). Trust is the product.

## Target users and personas

| Persona | Context | Pain today | What wins them |
|---|---|---|---|
| **Olga, club organizer** (primary) | Sports club / choir / volunteer association, 20–300 members, volunteer admins | Members overwrite fixtures; board meetings must be hidden; sends PDFs of dates | Group roles, `contribute` level (members add own entries only), locked official entries, iCal feed for members, imports from league website |
| **Tom, small team lead** | 5–30 person company team or agency | Outlook/Google sharing too coarse; uses 4 calendars for one team | Per-event overrides, "Busy-only" entries, Team plan with audit log |
| **Fiona, family organizer** | Family / shared flat, 2–8 people | Kids' school dates scattered; private entries leak into family calendar | Free plan, school website import, private entries in shared calendar |
| **Hugo, event aggregator** | Local culture blog, community manager | Copies events from 20 venue sites by hand | LLM import from many sources, dedupe, public read-only links |
| **Dev, integrator** | Builds tooling around calendars | No fine-grained calendar API | Clean versioned REST API, tokens, webhooks |

Primary market: **German-speaking Europe and EU** first (clubs/"Vereine" are a large, under-served segment; GDPR-native hosting in the EU is a selling point), English from day one.

## Positioning

| | Google Calendar / Outlook | Teamup | Calendly | **sCalenderPlus** |
|---|---|---|---|---|
| Core model | Personal calendars, calendar-level sharing | Shared calendar with sub-calendars and access keys | Booking pages | Shared calendar with **per-event permissions** |
| Per-entry permissions | No (only "private" flag for own calendar) | Partly via sub-calendars | n/a | **Yes: per user, group, role, link** |
| Groups with roles | Workspace admin only | Limited | Teams (paid) | **Yes, first-class** |
| Native app integration | Native | iCal feeds | Calendar sync | **Personalised iCal feeds with access labels; CalDAV later** |
| Automated imports | ICS subscription only | ICS | n/a | **ICS + JSON-LD + LLM extraction from any event website, scheduled** |
| Price point | Bundled | ~€8–€40/month per calendar | ~€10–€16/seat | Free tier + Pro €12/month + Team per seat |
| Hosting | US big tech | EU/US | US | **EU hosting, self-hostable** |

**Key differentiator:** *"Same calendar, different rules per entry."* Secondary: self-filling calendars via LLM import, and honest native-app integration (the event tells you whether you may edit it).

We do **not** compete on: meeting scheduling/booking pages (Calendly), email+calendar suites, resource booking for enterprises. Booking pages may come later as a Pro add-on, not a core pillar.

## Product principles

1. **Secure by default, explainable always.** New calendars are private; every access is explainable; downgrades never expose data.
2. **The calendar app you already use is a client.** Feature design starts with "how does this look in an iCal feed?"
3. **Free must be genuinely useful forever.** We gate *power* (scale, automation, fine-grained control), not *time*.
4. **Opinionated defaults, few knobs.** Roles and levels are a small totally-ordered set, not an RBAC matrix.
5. **API-first.** The web app uses the same public, versioned API that native apps and integrators use.
6. **EU-grade privacy.** Data minimisation, export and deletion are features, not chores.

## Success metrics (first 12 months after public launch)

| Metric | Target |
|---|---|
| Weekly active calendars with ≥2 users | 2,000 |
| Share of active calendars using ≥1 event override | ≥ 30 % (validates the core idea) |
| Feed subscriptions per active user | ≥ 1.2 |
| Free → paid conversion (accounts active > 30 days) | 3–5 % |
| LLM import precision (events accepted unchanged in review) | ≥ 90 % |
| p95 API latency (event list, 1 month window) | < 150 ms |

## Non-goals (for now)

- Email client, tasks/to-do app, video meetings.
- Enterprise resource/room booking.
- Offline-first native apps (PWA caches read-only views; full offline sync arrives with native apps).
- Arbitrary custom permission matrices (we keep the ordered model; see permissions §6.3).
