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
| 4 | `manage` | Edit + change the event's permission overrides, move the event to another calendar (needs `contribute` on the target). **Only reachable through a floor** (calendar `manage`/`owner` or creator floor, §4.2); overrides can grant at most `edit`. |

### 2.2 Calendar levels

| # | Level | Capabilities on the calendar | Implied default event level |
|---|---|---|---|
| 0 | `none` | Calendar invisible. | `none` |
| 1 | `free_busy` | Sees the calendar exists and busy blocks. | `free_busy` |
| 2 | `read` | Sees calendar and events. | `read` |
| 3 | `contribute` | Read + **create** events. Creator floor (§4.2) gives `manage` on own events. | `read` |
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

Anonymous principals (share-link holders; "public" always means a share link, there is no separate public flag) are capped at **`min(read, link level)`**: the link's level is a ceiling, not only a base. A share link belongs to exactly one calendar: its holder has `Lc = link level` there and `none` on every other calendar. Signed-in users never act through a share link (a share-link feed is anonymous whoever fetches it), so a principal is either a user (with group memberships) or a link holder, never both.

Calendar grants name only `user` and `group` principals (levels `free_busy` … `manage`); link holders reach a calendar only through its share links, and `everyone` cannot be granted (it is defined by the calendar's audience).

`everyone` and `anonymous` overrides are **restrict-only**: the engine applies `min(override, base)`. Elevation is only possible through explicit `user`/`group` overrides. (Otherwise a contributor could turn a `free_busy` share link or a `free_busy` calendar audience into `read` for their event.)

`everyone` and `anonymous` are **scoped to the calendar's existing audience**. They never pull in strangers; elevating someone without calendar access requires an explicit `user` or `group` override (rule 7):

```text
matches(everyone, U)  := Lc(U, C) >= free_busy  or  U holds a share link / public access to C
matches(anonymous, U) := U is anonymous and holds a share link / public access to C
matches(group:G[r], U):= U is member of G with role >= r
matches(user:X, U)    := U == X
```

## 4. The resolution algorithm

### 4.1 Calendar effective level `Lc(U, C)`

```text
Lc(U, C):
  if C.owner == U or (C.owner is group G and U.role(G) == owner): return owner
  levels = [g.level for g in C.grants if matches(g.principal, U)]
  if C.owner is group G and U ∈ G: levels += [C.groupRoleDefaults[U.role(G)]]   # §6.2
  return max(levels, default = none)
```

Calendar grants are **purely additive (max)**. There is no deny at calendar level: if you do not want someone to see a calendar, do not grant it (or remove them from the group). Rationale: calendar sharing is coarse and must be predictable; exceptions belong at the event level where they are explicit and visible.

### 4.2 Event effective level `Le(U, E)` — "most specific override wins, then floors"

```text
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
      # level REPLACES base: lower (restrict) or, for user/group tiers only, higher (elevate)
      if topTier <= tier(anonymous): level = min(level, base)   # everyone/anonymous: restrict-only

  # Step 4 – caps
  level = min(level, edit)                           # manage only via floors (step 1)
  if U is anonymous: level = min(level, read, U.linkLevel)
  return level
```

Notes: the creator test uses the event's `creator_user_id` (null for system/import events and tombstoned creators: no floor). For link holders the last cap never binds today (their base is ≤ the link level and their overrides are restrict-only); it stays as defence in depth. The result does not depend on the order of grants or overrides.

### 4.3 Rules in plain language

1. **Most specific wins.** A user-level override beats any group override, which beats an `anonymous` override, which beats an `everyone` override, which beats the calendar grant.
2. **Ties are unions.** Several matching rules on the same tier → the highest level wins (a user in two groups gets the better of the two).
3. **Overrides replace, they do not merge.** An override is a complete statement for the principals it matches. This allows both *restricting* ("this board meeting is `free_busy` for everyone") and *elevating* ("Anna may edit this one event although she only reads the calendar"). Elevation works only on `user`/`group` tiers and only up to `edit`; `everyone`/`anonymous` overrides only restrict.
   Consequence of tiering: a `group:A → none` override hides the event from a member of A even if they also reach the calendar via group B. The explainer shows this.
4. **Overrides CAN reduce access below the calendar level** — this is the product's core promise (private entries in a shared calendar). Explicit `none` on a more specific tier is our deny mechanism; there is no separate deny flag.
5. **Overrides can never lock out managers.** Calendar `manage`/`owner` is a floor. Otherwise a contributor could hide an event from the people responsible for the calendar, and nobody could repair it.
6. **Creators keep control of their own events** (`manage` floor) as long as they still have at least `contribute` on the calendar. Removing someone from the calendar removes their creator rights too. The floor can be disabled per calendar (`creatorsManageOwnEvents = false`) for strictly curated calendars.
7. **Overrides can grant access to people without calendar access** ("external sharing"). Such events appear in the user's virtual calendar **"Shared with me"**. This enables "share one event, not the calendar". Because it moves data outside the calendar's audience, only calendar managers may do it, unless the calendar setting `creatorsMayShareExternally` (default `false`) allows creators too.
8. **Anonymous ≤ min(read, link level)**, always.
9. **`manage` on an event comes only from floors.** Overrides cap at `edit`, so access can never be chained ("an override-manager grants manage to a stranger who grants…").

### 4.4 Who can change event permissions

| Actor | May create/modify/delete overrides on event E |
|---|---|
| Calendar owner / calendar `manage` | Yes, on every event of the calendar. |
| Event creator with creator floor | Yes, on own events — but no *external* principals unless `creatorsMayShareExternally`. |
| `edit` or below (incl. override-granted `edit`) | No. |

Constraints:

- Override levels are `none | free_busy | read | edit` (no `manage`, rule 9).
- **External principal** = a principal whose matched people are not all guaranteed `Lc ≥ read`: a `user` with `Lc < read`, or a `group:G[r]` whose *guaranteed* level is below `read` — the guaranteed level is the maximum of (if G owns the calendar) the lowest role default among roles ≥ r and the grants to `G[r']` with r' ≤ r. So `group:Owner[viewer]` is external when the viewer default is `free_busy`, and `group:G[viewer]` is external when only `G[member]` holds a `read` grant. `everyone`/`anonymous` are never external (§3).
- An override is **external sharing** when its principal is external *and* its level exceeds what the calendar already gives that principal (`level > impliedEventLevel(guaranteed level)`); restricting outsiders (e.g. `user:X → none`) is not sharing. External sharing requires calendar `manage` (or the setting above) → else `403 external_sharing_not_allowed`.
- Overrides are replaced as a whole set (`PUT`). Entries that stay unchanged or are only lowered are not re-checked for principal selection and external sharing (they decide nothing new, e.g. a creator keeps or narrows a share a manager made); added and raised entries are. The same principal twice, or a level above `edit` → `422`.
- Removing entries is allowed to rights holders — also a creator lifting a restriction a manager put on the creator's own event: the people concerned get back what the calendar gives them, and the creator floor is `manage` on that event (calendars that need restrictions creators cannot lift disable the floor, `creatorsManageOwnEvents = false`). One exception for actors without external rights: removing the `user` entry of an outsider (`Lc < read`) is external sharing while a `group` entry that shares externally stays above both the removed level and what the calendar gives that user — the group share would then decide for them (e.g. a manager shares with a partner group but excludes one partner). The engine does not know other people's memberships, so it assumes membership; the answer reveals none.
- Principal selection: `group` principals must be groups the actor belongs to, the owner group, or groups that hold a grant on the calendar (→ else `422`). `user` principals are picked from people sharing a group/calendar with the actor, or by exact email; an unknown email becomes a *pending share* activated on verified sign-up (same mechanism as pending invites). Responses never reveal whether an email has an account.
- **Attendees** (v1): inviting an internal user who lacks `Le ≥ read` creates a `user → read` override and therefore needs override rights (+ the external rule). `edit` users may only add attendees who can already read the event.
- Self-lockout cannot happen by construction (only floor holders edit overrides, and floors ignore overrides); the engine asserts it (debug assertion + property test) and the API keeps `409 permission_self_lockout` as a defensive code.
- Overrides of a recurrence exception are those of its series (exceptions have no ACL of their own, §4.6).
- Overrides count against the **calendar owner's plan** (see [plans](../product/plans.md)). On downgrade, existing overrides **remain enforced** (removing them could leak private events); only creating new ones is blocked.
- Every change is written to the audit log with before/after.

### 4.5 Calendar-level actions

| Action | Required `Lc` |
|---|---|
| See calendar in list | `free_busy` |
| Create event | `contribute` |
| Edit calendar name/color/timezone/settings | `manage` |
| Add/remove grants, share links, edit role defaults | `manage` (cannot grant `owner`; cannot grant above own level) |
| Delete / transfer calendar | `owner` |
| Configure LLM import into the calendar | `manage` |

### 4.6 Lifecycle rules (permission-relevant)

| Situation | Rule |
|---|---|
| **Move event** to calendar T | Requires `Le = manage` on E and `Lc(T) ≥ contribute`. Overrides travel with the event and are **re-validated as if the mover set them in T** (external rule, T owner's plan limits): external sharing in T needs the mover's `Lc(T) ≥ manage`, or the creator floor in T with T's `creatorsMayShareExternally`; a mover without a floor on the event in T (not its creator, or T disables the creator floor) could set no override there, so none may travel → else `409 override_invalid_in_target` listing them (the mover removes them first or asks a manager of T). UID clash in T → `409`. Both calendars' `acl_version` bumped; `calendar_changes` records delete in source, upsert in T. |
| **Split series** ("this and following") | New series keeps the original `creator_user_id` (an `edit` user who splits must not gain the creator floor) and copies the overrides; the copy is allowed even when over the plan limit (no new privacy decision). |
| **Recurrence exceptions** | Inherit the series ACL (MVP); changing series overrides affects all occurrences, past included. |
| **Removed from group / demoted / grant removed** | User `acl_version` bumped → effective immediately incl. feeds. Creator floor lapses with `Lc < contribute`. `user:` overrides naming that user on events of the affected calendars are **deleted by default** (remover can untick "also revoke their individual event shares"); audited. |
| **User deleted** (after grace) | Their grants, overrides and tokens are deleted (feeds → `410`); `creator_user_id` becomes a tombstone (no floor); owned calendars/groups must be transferred first, otherwise deleted with notice to members; their import sources move to the calendar owner or pause. |
| **Import source creator loses `manage`** | Next run pauses the source and notifies calendar managers. |
| **Calendar / group ownership transfer** | New owner's plan applies immediately (downgrade rules in plans.md); UI warns before confirming. |

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
| other members (role `member`) | read | group tier (their calendar level `contribute` implies `read` anyway) |

### Example E – sharing a single event with Eve: override `user:Eve → read`

Eve gets **read**; the event appears in her "Shared with me" virtual calendar and her aggregated iCal feed. She still cannot see the calendar or any other event.

### Example F – contributor attempts to hide an event from admins

Mia sets `group:Lions → none` on her event. Olga/Adam keep `manage` (floor). Mia keeps `manage`. Vic → `none`. The link holder keeps `free_busy` (the group override does not match link holders; `anonymous → none` would hide it from them too). This is permitted; managers are never locked out.

### Example G – contributor tries to widen access

Mia sets `everyone → read` on her event: Vic stays `read`, the link holder stays **`free_busy`** (restrict-only + link ceiling). Mia sets `user:Eve → read`: rejected `403 external_sharing_not_allowed` (Eve has `Lc = none`; calendar has `creatorsMayShareExternally = false`). Adam (manager) may add it. Mia sets `user:Vic → manage`: rejected `422` (max `edit`).

## 6. Groups and roles

### 6.1 Group roles (fixed in MVP)

| Role | Group administration | Default level on group-owned calendars |
|---|---|---|
| `owner` (1+) | Everything incl. delete group, assign every role (also `owner`), remove other owners. | owner |
| `admin` | Invite/remove `member`/`viewer` and switch them between `viewer` and `member`; cannot promote to `admin`/`owner` (only owners do) and cannot demote/remove other admins or owners. Invite *links* carry at most `member`. Create group calendars. | manage |
| `member` | See member list, leave group. | contribute |
| `viewer` | See member list (configurable), leave group. | read |

Every member may leave and lower their own role; nobody raises their own. A group always keeps at least one owner (the last owner cannot leave, be demoted or removed → `409 last_owner`). The **billing owner** (`groups.owner_user_id`, see §6.2) stays an owner until they transfer billing to another owner (`POST /groups/{id}/transfer`, only by the billing owner, only to a role-owner); before that they cannot leave or be demoted (`409 billing_owner_transfer_required`). Implemented as pure rules in `Core/Groups/MembershipPolicy`.

### 6.2 Per-calendar role defaults

Each group-owned calendar stores `groupRoleDefaults` (admin/member/viewer → calendar level), editable by calendar managers (never above their own level). Exactly one role-owner is the **billing owner** (`groups.owner_user_id`) whose plan governs; transferable among owners. Example: an "Official fixtures" calendar sets `member → read` so only admins edit it. Owner is always `owner`.

### 6.3 Custom roles (Team plan, later)

A custom role is a named rank inserted into the order (e.g. `coach` between member and admin) plus capability flags for group administration. Calendar defaults and `minRole` matching work unchanged because roles stay totally ordered. We deliberately avoid arbitrary capability sets (RBAC soup) to keep the algorithm explainable.

## 7. iCal / CalDAV projection

Feeds are always personalised (token belongs to a user, or to an anonymous share link), so the algorithm runs per viewer.

| Effective level | iCal output |
|---|---|
| `none` | VEVENT omitted. |
| `free_busy` | `SUMMARY:Busy` (localized), no DESCRIPTION/LOCATION/ATTENDEE/URL/CATEGORIES/X-SCALENDERPLUS-*, `CLASS:CONFIDENTIAL`, times + `TRANSP` kept; `UID` replaced by the opaque `{eventId}@scalenderplus` (imported UIDs can contain text). Exception VEVENTs are stripped the same way. Transparent events omitted. |
| `read` | Full event; label "read-only" per feed label settings (default: title prefix `🔒` followed by a space). |
| `edit` / `manage` | Full event; label "editable" (default: no title marker, description footer only). |

Every non-busy VEVENT gets a description footer line, e.g.
`— sCalenderPlus: you can edit this event · https://app.example.com/e/{id}` or
`— sCalenderPlus: read-only for you`. A machine-readable `X-SCALENDERPLUS-ACCESS:read|edit|manage` property is added for our own native apps. Label modes and the CalDAV mapping are specified in [ical-caldav.md](ical-caldav.md).

## 8. Implementation notes

- **Pure engine** (`SCalenderPlus.Core/Permissions`): `PermissionEngine.Resolve(PrincipalContext, CalendarAcl, EventAcl) → EventAccess` (level, `Lc`, trace) and `PermissionEngine.ResolveCalendar(PrincipalContext, CalendarAcl) → CalendarAccess` for the explainer; `ResolveLevel`/`ResolveCalendarLevel` run the same code without a trace and allocate nothing (listings, feeds: ~40 ns per event with a few overrides vs ~280 ns and ~800 B traced); `OverridePolicy` (rights, external sharing, principal selection, `EvaluateChange` for a proposed override set, `InvalidInTarget` for moves) and `AccessPolicy` (required levels of calendar/event actions, 404-vs-403, grants/role defaults/share links). Inputs are plain objects loaded in batch; no EF types, no I/O. Every resolution returns its steps (`ResolutionStep`: share link, owner, matched grants, role default, floors, base, matched overrides with tier, applied tier, restrict-only, caps, result) — the explain endpoint renders them. 100 % line and branch coverage (CI gate) plus property tests (CsCheck) for invariants:
  - managers/owner never get less than `manage`; floors are never lowered by overrides;
  - `manage` comes only from a floor (no override yields `manage`, even a stored one);
  - anonymous never exceeds `read` nor the link level; a link of another calendar sees nothing;
  - adding a calendar grant never reduces any calendar level, nor any event level (monotonic);
  - an event without overrides equals `impliedEventLevel(Lc)` unless a floor applies;
  - `everyone`/`anonymous` overrides never elevate and never reach principals outside the calendar's audience;
  - a matching `user` override decides alone (capped at `edit`) unless a floor applies;
  - grant/override order does not matter; exceptions resolve like their series;
  - only floor holders may change overrides; an allowed change never locks its actor out; without external rights no change (additions, raises or removals) leaves an outsider above what they had before or what the calendar gives them;
  - adding an override changes only the principals it matches, and adding a `none` override never raises anyone;
  - a move carries only overrides the mover could set in the target.
- **Listing performance**: for a time window, (1) compute `Lc` for all calendars visible to the user (small set, cached per request), (2) query events in window for calendars with `Lc ≥ free_busy` **union** events having a `user`/`group` override matching the user (index on `event_overrides(principal_type, principal_id)`), (3) batch-load overrides only for events with `has_overrides = true`, (4) resolve in memory and drop `none`. Implemented by `EventQueryService.WindowAsync` (api.md §4 "Event window"; step (2)'s union through `IEventOverrideSource.EventsNamingAsync` over that index).
- **Cache invalidation**: `users.acl_version` (bumped on that user's membership/role change and on any grant or override naming the user), `groups.acl_version` (bumped on overrides/grants naming the group) and `calendars.acl_version` (bumped on grant, override, share link, role default, `creators*` setting, ownership, freeze change). Feed ETags and per-request caches key on user + user's groups + included calendars. Token validity is checked on **every** request before any cache lookup.
- **Single choke point**: event rows are read only through the permission-aware `Application/Events/EventQueryService` (and added only through its write-side counterpart `EventWriter`, which also appends the sync log); calendars through `CalendarAccessLoader`. The architecture test `EventAccessTests` scans the IL of Application, Infrastructure, Api and Worker and fails on any other reference to `DbSet<Event>` (`IAppDbContext.Events`, `Set<Event>()`, fields/properties, lambdas and expression trees) — tenant isolation. Event overrides are read through `IEventOverrideSource` (`event_overrides`) and changed by `EventOverrideService` (api.md "Event overrides").
- **Derived surfaces** (search, availability, notifications, digests, reminders, webhooks, exports) evaluate `Le` at send/query time; text search and reminders only consider events with `Le ≥ read` (search on `free_busy` events would leak titles by matching).
- **Error semantics**: `none` → `404 Not Found` (never reveal existence); insufficient but visible → `403 Forbidden` with problem type `.../insufficient-permission` and the required level.
- **Level enums**: calendar and event levels are separate enums (`CalendarLevel` 0–6, `EventLevel` 0–4) whose numbers differ for the same name; never compare across them, map via `impliedEventLevel`.
- **Explainability**: `GET /api/v1/events/{id}/access/explain?userId=` (managers only) returns the steps (floor/base/matched tier/caps). The UI shows "Why can X see this?" — essential for trust in a non-trivial model.
