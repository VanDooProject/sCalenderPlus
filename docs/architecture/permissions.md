# Permission Model

> This is the core of sCalenderPlus: **different permissions for different events inside the same calendar.**
> Everything else (UI, iCal, CalDAV, API) is a projection of the algorithm defined here.
> The algorithm lives in one pure, dependency-free module (`SCalenderPlus.Core/Permissions`) and is the
> single source of truth. No endpoint, feed or DAV handler may implement its own access logic.

## 1. Vocabulary

| Term | Meaning |
|---|---|
| **Principal** | Who is asking: a user, a member of a group (with a role), an anonymous share-link holder, or the public. |
| **Resource** | What is accessed: a **calendar** or an **event** (a recurring series counts as one event). |
| **Grant** | A calendar-level rule: *principal → level* on a calendar. Grants are additive. |
| **Override** | An event-level rule: *principal → level* on a single event. Overrides are exceptions and **replace** the calendar-derived level for the principals they match. |
| **Floor** | A level a principal always keeps regardless of overrides (owner, managers, creator). |
| **Effective level** | The final result of the algorithm for (principal, event) or (principal, calendar). |

## 2. Permission levels

Levels are totally ordered. A higher level includes every capability of the lower ones.

### 2.1 Event levels

| # | Level | Capabilities on the event |
|---|---|---|
| 0 | `none` | Event does not exist for this principal. Not listed, not in feeds, not counted, 404 on direct access. |
| 1 | `free_busy` | Sees start/end, all-day flag and transparency only. Title rendered as "Busy" (localized). Opaque events only; transparent ("free") events are omitted entirely. |
| 2 | `read` | Sees all details: title, description, location, attendees, attachments, reminders of their own. Can RSVP if invited. |
| 3 | `edit` | Read + change fields, times, recurrence, attendees; delete the event or single occurrences. |
| 4 | `manage` | Edit + change the event's permission overrides, move the event to another calendar (needs `contribute` on the target). |

### 2.2 Calendar levels

| # | Level | Capabilities on the calendar | Implied default event level |
|---|---|---|---|
| 0 | `none` | Calendar invisible. | `none` |
| 1 | `free_busy` | Sees the calendar exists and busy blocks. | `free_busy` |
| 2 | `read` | Sees calendar and events. | `read` |
| 3 | `contribute` | Read + **create** events. Creator floor (§4.3) gives `manage` on own events. | `read` |
| 4 | `edit` | Create events and edit every event (subject to overrides). | `edit` |
| 5 | `manage` | Edit + calendar settings, grants, share links, any event's overrides. Floor: cannot be reduced by overrides. | `manage` |
| 6 | `owner` | Exactly one principal (a user or a group). Manage + delete/transfer calendar; plan limits of the owner apply. | `manage` |

`contribute` exists because the most common club/team pattern is "everyone may add their own entries, but must not touch other people's". Without it, organizers would need an override on every event.

## 3. Principals and matching

| Principal type | Matches | Specificity tier (higher = more specific) |
|---|---|---|
| `user:{id}` | That user. | **4** |
| `group:{id}[minRole]` | Members of group `id` whose role ≥ `minRole` (default `viewer`, i.e. all members). | **3** |
| `anonymous` | Share-link holders and the public (unauthenticated). | **2** |
| `everyone` | Every principal that reaches the event (all of the above). | **1** |

Group roles are ordered `viewer < member < admin < owner` (see §6). There are **no nested groups** in MVP (avoids cycles and expensive resolution; revisit with Team plan "departments").

Anonymous principals (share links, public calendars) are **hard-capped at `read`**, regardless of grants or overrides.

`everyone` and `anonymous` are **scoped to the calendar's existing audience**. They never pull in strangers; elevating someone without calendar access requires an explicit `user` or `group` override (rule 7):

```
matches(everyone, U)  := Lc(U, C) >= free_busy  or  U holds a share link / public access to C
matches(anonymous, U) := U is anonymous and holds a share link / public access to C
matches(group:G[r], U):= U is member of G with role >= r
matches(user:X, U)    := U == X
```

## 4. The resolution algorithm

### 4.1 Calendar effective level `Lc(U, C)`

```
Lc(U, C):
  if C.owner == U or (C.owner is group G and U.role(G) == owner): return owner
  levels = [g.level for g in C.grants if matches(g.principal, U)]
  if C.owner is group G and U ∈ G: levels += [C.groupRoleDefaults[U.role(G)]]   # §6.2
  return max(levels, default = none)
```

Calendar grants are **purely additive (max)**. There is no deny at calendar level: if you do not want someone to see a calendar, do not grant it (or remove them from the group). Rationale: calendar sharing is coarse and must be predictable; exceptions belong at the event level where they are explicit and visible.

### 4.2 Event effective level `Le(U, E)` — "most specific override wins, then floors"

```
Le(U, E):                                   # E belongs to calendar C
  if E is a recurrence exception: return Le(U, E.series)   # exceptions inherit the series ACL (MVP)
  lc = Lc(U, C)

  # Step 1 – floors that overrides can never reduce
  if lc >= manage:                                   return manage
  if E.creator == U and lc >= contribute
     and C.creatorsManageOwnEvents:                  return manage

  # Step 2 – base level derived from the calendar
  base = impliedEventLevel(lc)                       # table §2.2

  # Step 3 – event overrides, most specific tier wins
  matching = [o for o in E.overrides if matches(o.principal, U)]
  if matching is empty:
      level = base
  else:
      topTier = max(tier(o.principal) for o in matching)
      level   = max(o.level for o in matching if tier(o.principal) == topTier)
      # level REPLACES base: it can be lower (restrict) or higher (elevate)

  # Step 4 – caps
  if U is anonymous: level = min(level, read)
  return level
```

### 4.3 Rules in plain language

1. **Most specific wins.** A user-level override beats any group override, which beats an `anonymous` override, which beats an `everyone` override, which beats the calendar grant.
2. **Ties are unions.** Several matching rules on the same tier → the highest level wins (a user in two groups gets the better of the two).
3. **Overrides replace, they do not merge.** An override is a complete statement for the principals it matches. This allows both *restricting* ("this board meeting is `free_busy` for everyone") and *elevating* ("Anna may edit this one event although she only reads the calendar").
4. **Overrides CAN reduce access below the calendar level** — this is the product's core promise (private entries in a shared calendar). Explicit `none` on a more specific tier is our deny mechanism; there is no separate deny flag.
5. **Overrides can never lock out managers.** Calendar `manage`/`owner` is a floor. Otherwise a contributor could hide an event from the people responsible for the calendar, and nobody could repair it.
6. **Creators keep control of their own events** (`manage` floor) as long as they still have at least `contribute` on the calendar. Removing someone from the calendar removes their creator rights too. The floor can be disabled per calendar (`creatorsManageOwnEvents = false`) for strictly curated calendars.
7. **Overrides can grant access to people without calendar access.** Such events appear in the user's virtual calendar **"Shared with me"**. This enables "share one event, not the calendar".
8. **Anonymous ≤ read**, always.

### 4.4 Who can change event permissions

| Actor | May create/modify/delete overrides on event E |
|---|---|
| Calendar owner / calendar `manage` | Yes, on every event of the calendar. |
| Event creator with creator floor | Yes, on own events. |
| Anyone with effective `manage` on E via override | Yes. |
| `edit` or below | No. |

Constraints:

- Nobody can create an override that would reduce **their own** level (prevents accidental self-lockout; the UI shows a warning, the API returns `409 permission-self-lockout`).
- Overrides count against the **calendar owner's plan** (see [plans](../product/plans.md)). On downgrade, existing overrides **remain enforced** (removing them could leak private events); only creating new ones is blocked.
- Every change is written to the audit log with before/after.

### 4.5 Calendar-level actions

| Action | Required `Lc` |
|---|---|
| See calendar in list | `free_busy` |
| Create event | `contribute` |
| Edit calendar name/color/timezone/settings | `manage` |
| Add/remove grants, share links | `manage` (cannot grant `owner`; cannot grant above own level) |
| Delete / transfer calendar | `owner` |
| Configure LLM import into the calendar | `manage` |

## 5. Worked examples

Setup: calendar **"FC Lions – Club"**, owned by group **Lions** (roles: Olga=owner, Adam=admin, Mia=member, Vic=viewer). External user Eve is in no group. Calendar settings: role defaults `admin→manage, member→contribute, viewer→read`, extra grant `anonymous → free_busy` via share link, `creatorsManageOwnEvents = true`.

Calendar levels: Olga `owner`, Adam `manage`, Mia `contribute`, Vic `read`, Eve `none`, link holder `free_busy`.

### Example A – normal event created by Mia, no overrides

| Principal | Floor? | Base | Override | Effective |
|---|---|---|---|---|
| Olga | owner → manage | – | – | **manage** |
| Adam | manage | – | – | **manage** |
| Mia | creator floor | – | – | **manage** |
| Vic | – | read | – | **read** |
| Eve | – | none | – | **none** |
| Link | – | free_busy | – | **free_busy** |

### Example B – "Board meeting" (created by Adam), overrides: `everyone → free_busy`, `group:Lions[admin] → read`

| Principal | Floor? | Base | Matching overrides (tier) | Effective |
|---|---|---|---|---|
| Olga | manage | | | **manage** |
| Adam | manage | | | **manage** |
| Mia | – | read | everyone(1)=free_busy | **free_busy** (restricted) |
| Vic | – | read | everyone(1)=free_busy | **free_busy** |
| Eve | – | none | – (`everyone` does not match, see §3) | **none** |
| Link | – | free_busy | everyone(1)=free_busy | **free_busy** |

Mia's and Vic's access is *reduced below* their calendar level; managers are unaffected (floor).

### Example C – "Coach's private note" (Mia), override `everyone → none`, `user:Vic → read`

| Principal | Effective | Why |
|---|---|---|
| Olga, Adam | manage | manager floor |
| Mia | manage | creator floor |
| Vic | **read** | user tier(4) beats everyone tier(1) |
| Link | **none** | everyone(1)=none |

### Example D – Mia's training, override `user:Vic → edit`, `group:Lions → read`

| Principal | Effective | Why |
|---|---|---|
| Vic | **edit** | user tier beats group tier; elevation above calendar `read` |
| Mia (creator) | manage | floor |
| other members | read | group tier |

### Example E – sharing a single event with Eve: override `user:Eve → read`

Eve gets **read**; the event appears in her "Shared with me" virtual calendar and her aggregated iCal feed. She still cannot see the calendar or any other event.

### Example F – contributor attempts to hide an event from admins

Mia sets `group:Lions → none` on her event. Olga/Adam keep `manage` (floor). Mia keeps `manage`. Vic → `none`. This is permitted; managers are never locked out.

## 6. Groups and roles

### 6.1 Group roles (fixed in MVP)

| Role | Group administration | Default level on group-owned calendars |
|---|---|---|
| `owner` (1+) | Everything incl. delete group, billing, transfer ownership. | owner |
| `admin` | Invite/remove members (not owners), change roles up to admin, create group calendars. | manage |
| `member` | See member list, leave group. | contribute |
| `viewer` | See member list (configurable), leave group. | read |

### 6.2 Per-calendar role defaults

Each group-owned calendar stores `groupRoleDefaults` (admin/member/viewer → calendar level), editable by calendar managers. Example: an "Official fixtures" calendar sets `member → read` so only admins edit it. Owner is always `owner`.

### 6.3 Custom roles (Team plan, later)

A custom role is a named rank inserted into the order (e.g. `coach` between member and admin) plus capability flags for group administration. Calendar defaults and `minRole` matching work unchanged because roles stay totally ordered. We deliberately avoid arbitrary capability sets (RBAC soup) to keep the algorithm explainable.

## 7. iCal / CalDAV projection

Feeds are always personalised (token belongs to a user, or to an anonymous share link), so the algorithm runs per viewer.

| Effective level | iCal output |
|---|---|
| `none` | VEVENT omitted. |
| `free_busy` | `SUMMARY:Busy` (localized), no DESCRIPTION/LOCATION/ATTENDEE/URL, `CLASS:CONFIDENTIAL`, times + `TRANSP` kept. Transparent events omitted. |
| `read` | Full event; label "read-only" per feed label settings (default: title prefix `🔒 `). |
| `edit` / `manage` | Full event; label "editable" (default: no title marker, description footer only). |

Every non-busy VEVENT gets a description footer line, e.g.
`— sCalenderPlus: you can edit this event · https://app.example.com/e/{id}` or
`— sCalenderPlus: read-only for you`. A machine-readable `X-SCALENDERPLUS-ACCESS:read|edit|manage` property is added for our own native apps. Label modes and the CalDAV mapping are specified in [ical-caldav.md](ical-caldav.md).

## 8. Implementation notes

- **Pure engine**: `PermissionEngine.Resolve(PrincipalContext, CalendarAcl, EventAcl) → Level`. Inputs are plain records loaded in batch; no EF types. 100% branch coverage plus property tests (FsCheck) for invariants:
  - managers/owner never get less than `manage`;
  - anonymous never exceeds `read`;
  - adding a calendar grant never reduces any calendar level (monotonic);
  - an event without overrides equals `impliedEventLevel(Lc)` unless a floor applies.
- **Listing performance**: for a time window, (1) compute `Lc` for all calendars visible to the user (small set, cached per request), (2) query events in window for calendars with `Lc ≥ free_busy` **union** events having a `user`/`group` override matching the user (index on `event_overrides(principal_type, principal_id)`), (3) batch-load overrides only for events with `has_overrides = true`, (4) resolve in memory and drop `none`.
- **Cache invalidation**: each user has an `acl_version` (bumped on group membership change) and each calendar an `acl_version` (bumped on grant/override change). Feed ETags and per-request caches key on them.
- **Error semantics**: `none` → `404 Not Found` (never reveal existence); insufficient but visible → `403 Forbidden` with problem type `.../insufficient-permission` and the required level.
- **Explainability**: `GET /api/v1/events/{id}/access/explain?userId=` (managers only) returns the steps (floor/base/matched tier/caps). The UI shows "Why can X see this?" — essential for trust in a non-trivial model.
