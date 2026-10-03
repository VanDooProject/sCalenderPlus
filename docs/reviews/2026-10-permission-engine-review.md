# Permission Engine Review — October 2026

Scope: the pure permission engine (`backend/src/SCalenderPlus.Core/Permissions`: `PermissionEngine`, `OverridePolicy`, `AccessPolicy`, model types), the plan-limit rules of `Core/Entitlements` that gate override changes, their tests, and the spec changes the implementer made in `dbcb694` ([permissions.md](../architecture/permissions.md)). Reviewer stance: adversarial — look for inputs where someone gets more than they should, where a restriction set by an authorised person can be undone by a less-privileged one, where outsiders or link holders gain visibility, or where managers can be locked out; then product sanity, readability, the explainer trace and hot-path performance. Severity: **high** = data exposure without preconditions; **med** = exposure or escalation with preconditions; **low** = robustness, performance, defence in depth; **info** = by design, documented.

Method: read the engine against §3–§4.6 line by line; wrote the scenarios below as table tests (`ScenarioTests`); turned every suspected escalation into a CsCheck property over random worlds (4 users, 3 groups, random grants, role defaults, settings, overrides incl. stored `manage`, exceptions, share links) and confirmed the two findings by running the new properties against the unfixed code (both falsified within the first iterations).

## 1. Scenarios

Every row is a test in `ScenarioTests` (traced and untraced resolution); *actual* equals *expected* for all rows. Rows marked *(surprising)* match the spec but may surprise users — see §4.

| Scenario | Setup | Who | Expected | Actual |
|---|---|---|---|---|
| Family, private entry | Dad owns, Mom `manage`, kids (`group:Family[member]`) `contribute`, Grandma `free_busy`; Tom's diary `everyone → none` | Tom / Lisa / Grandma / Dad, Mom | manage / none / none / manage | same |
| Family, plain entry | Tom's dentist, no overrides | Lisa / Grandma | read / free_busy | same |
| Family, surprise for a sibling | Lisa's party `user:Tom → none` | Tom / Grandma | none / free_busy | same |
| Family, surprise for the owner *(surprising)* | Mom's party `user:Dad → free_busy` | Dad | manage (rule 5: owners are never restricted) | same |
| Club, member's private entry | group-owned (defaults), `free_busy` link; Max's physio `everyone → none` | Max / Mia, Vic / Adam / link | manage / none / manage / none | same |
| Club, other people's entries | Mia's training, no overrides | Max (member) / Vic / link | read / read / free_busy | same |
| Club, legacy row | system event, stored `user:Max → manage` | Max / Mia | edit (capped) / read (no creator) | same |
| Owner leaving / demoted | Olga's board meeting `everyone → none` | Olga / after leaving / as admin | manage / none / manage | same |
| Creator demoted to viewer *(surprising)* | Mia's note `everyone → none` | Mia (member) / Mia (viewer) | manage / none (floor lapsed, her own restriction applies) | same |
| Company, HR-confidential | Acme-owned (admin manage, member read, viewer free_busy), `group:HR[member]` contribute; review `everyone → free_busy`, `user:Bob → read`, `group:HR → read` | Hanna (creator) / Bob / Carl / Hugo (HR) / intern / team lead | manage / read / free_busy / read / free_busy / manage | same |
| Company, HR-only | `everyone → none`, `group:HR → read` | Carl / Hugo / intern | none / read / none | same |
| Public calendar | Org owns, Staff `edit`, public `read` link, `free_busy` link, link of another calendar, signed-in stranger | links / other link / stranger / Staff (creator) | read, free_busy / none / none / manage | same |
| Public, internal planning | `anonymous → none` | public link / Staff | none / manage | same |
| Public, ticket sale *(surprising)* | `everyone → free_busy`, `anonymous → read` | public link / busy link / Staff (`edit`, not creator) | read / free_busy / free_busy | same |
| Two groups | grants `group:A → read`, `group:B[member] → edit`; Pat: A member, B viewer; Quinn: A and B member | Pat / Quinn | read / edit | same |
| Two groups, hide from A *(surprising)* | `group:A → none` | Pat / Quinn | none / none (rule 3: tier decides, Quinn's B grant does not help) | same |
| Two groups, tie | `group:A → none`, `group:B → read` | Pat / Quinn | read / read (union) | same |
| Recurring, exception moved by someone else | series by Mia `user:Vic → edit`; Vic moves one occurrence; Vic splits the series | Vic / Max / Mia / link / demoted Mia | edit / read / manage / free_busy / read, for exception and split alike; Vic may not change overrides (403) | same |
| Creator lifts a manager's restriction | manager put `everyone → free_busy` on Mia's event; Mia removes it | verdict, Vic afterwards | allowed, read | same |
| Creator removes an exclusion from an external share | manager set `group:Partners → read` (external) and `user:Eve → none`; Mia removes `user:Eve` | verdict | 403 `external_sharing_not_allowed` | was allowed → **fixed (P1)** |
| Contributor moves into a curated calendar | Mia's event `user:Vic → edit`, `everyone → none` into a calendar with `creatorsManageOwnEvents = false` | invalid overrides | both | were none → **fixed (P2)** |

## 2. Findings

| # | Sev | Area | Finding | Resolution |
|---|---|---|---|---|
| P1 | med | `OverridePolicy.EvaluateChange` (§4.4 atomic replace) | "Removing entries is always allowed" let a creator without external rights undo a manager's exclusion from an external share: with `group:Partners → read` (external, set by a manager) and `user:Eve → none`, removing the user entry hands the decision for Eve to the group tier — Eve, an outsider, gains `read`. The existing property only started from an empty event and missed it. | **Fixed** (`3b5e229`): removing the `user` entry of an outsider (`Lc < read`) is external sharing (`RemovalExposesExternalShare` → `403 external_sharing_not_allowed`) while a remaining external `group` entry gives more than the removed entry and what the calendar gives that user. Membership of others is unknown to the engine, so it is assumed (conservative; the answer reveals no membership). Removing restrictions *inside* the audience stays allowed (see §3, decision 9). New property: from any existing set, no allowed change by a creator without external rights leaves an outsider above max(before, calendar). |
| P2 | med | `OverridePolicy.InvalidInTarget` (§4.6 move) | Re-validation checked only the external rule. A mover without override rights in the target — a contributor moving an event they did not create, or any contributor moving into a curated calendar (`creatorsManageOwnEvents = false`) — carried overrides they could never set there, e.g. `user:Vic → edit` (elevation in a calendar where only managers decide) or hiding entries. | **Fixed** (`3b5e229`): without a floor on the event in the target, every override is listed (`409 override_invalid_in_target`; the mover removes them first or asks a manager of the target). Spec §4.6 updated. New property: a move carries only overrides the mover could set in the target with `EvaluateChange`. |
| P3 | low | `PermissionEngine` (hot path) | Every resolution built a trace (list + one record per step + a temporary list of matched overrides) and enumerated `IReadOnlyList` inputs with boxed enumerators: ~280 ns and ~800 B per event with three overrides. Window queries and feeds resolve every event of a listing. | **Fixed** (`2447639`): `ResolveLevel`/`ResolveCalendarLevel` run the same code with a null trace (`steps?.Add(new …)` neither allocates the step nor the list), matched overrides are traced in a second pass only when tracing, index loops. ~40 ns, 0 B per event (local micro timing, Release, 10⁶ calls). A test asserts that 10 000 untraced resolutions allocate nothing, a property that traced and untraced agree. Callers of listings and feeds should use the untraced API; `OverridePolicy`'s debug assertion does. |
| P4 | low | `IsExternalSharing` | The external-sharing refusal tells a creator whether a user they name has `read` on the calendar (403 vs allowed). | **Accepted**: creators are calendar members; principal selection limits `user` principals to people sharing a group/calendar with the actor or exact emails, and unknown emails, pending shares and accounts without access answer the same. |
| P5 | low | Trace / explainer | When a floor applies the trace stops at `ManagerFloor`/`CreatorFloor` and does not list the overrides it ignored, so "I set `user:Adam → none`, why does Adam still see it?" is answered only by the floor step. | **Not changed**: the floor step plus rule 5 is the answer; the explain endpoint can render "overrides do not apply to calendar managers/creators". Revisit if users ask. Otherwise the steps (share link, owner, grants, role default, floors incl. disabled/lapsed, base, matched overrides with tier and decisiveness, applied tier, restrict-only, caps, result) are sufficient and accurate for the endpoint. |
| P6 | info | §4.3 rule 1 | `anonymous` (tier 2) beats `everyone` (tier 1), so `everyone → free_busy` + `anonymous → read` shows details to public link holders but only busy blocks to a calendar editor who is not the creator (ticket-sale row). | **By design**, documented rule; the explainer shows the decisive tier. Pick `user`/`group` entries to open it to staff. |
| P7 | info | §4.3 rule 3 | A `group:A → none` hides the event from a member of A who has `edit` through group B (two-groups row). | **By design** (rule 3 states it); the explainer shows it. |

Checked and found sound: floors cannot be lowered and `manage` comes only from floors (also with stored `manage` overrides); link holders never exceed `min(read, link level)` and see nothing of other calendars; `user`/`group` overrides never match link holders; `everyone`/`anonymous` never elevate nor reach strangers; grant monotonicity for calendar and event levels; order independence; exceptions and splits keep the series ACL and creator; only floor holders change overrides (override-granted `edit` cannot); an allowed change never locks its actor out; raising an existing external share is re-checked; lowering never raises anyone (the tier stays); removing a `group` entry never raises anyone above what the calendar gives (lower tiers are restrict-only or the base); `AccessPolicy` 404/403 semantics, grant and role-default ceilings, share-link levels; `PlanLimits.CheckOverrideChange` (removals and lowerings always pass, also over the limit after a downgrade; first overrides of an active event count once) is consistent with the permission side (a removal refused by P1 is a permission refusal, not a plan one). Readability: the override loop now uses plain `if`/`else` instead of a nested conditional; naming follows the spec vocabulary.

## 3. Spec decisions of `dbcb694`

| # | Decision | Verdict |
|---|---|---|
| 1 | A share link belongs to one calendar: `Lc = link level` there, `none` elsewhere. | Confirmed (property: other calendars' links see nothing). |
| 2 | Signed-in users never act through a share link; a principal is a user or a link holder. | Confirmed. The UI should send members who open a public link to their own view (they would otherwise see the anonymous projection). |
| 3 | Grants name only `user`/`group`; `everyone` is not grantable. | Confirmed (otherwise `everyone` would be circular with the audience definition). |
| 4 | Null/tombstoned `creator_user_id` → no creator floor. | Confirmed (system/import events are managed by calendar managers). |
| 5 | The link cap never binds today; kept as defence in depth. | Confirmed by property (anonymous ≤ link level holds with and without restrict-only overrides). |
| 6 | The result does not depend on the order of grants or overrides. | Confirmed (property). |
| 7 | External principal via the *guaranteed* level of `group:G[r]` (lowest role default among roles ≥ r, grants to `G[r']` with r' ≤ r). | Confirmed: sound lower bound, conservative (user-level grants of members are ignored). |
| 8 | External sharing only when the level exceeds what the calendar gives the principal; restricting outsiders is not sharing. | Confirmed (property: refused exactly when it would raise an outsider). |
| 9 | Atomic replace: unchanged/lowered entries not re-checked, removals always allowed. | **Amended (P1)**. Removing restrictions on one's own event stays allowed — including a manager's restriction, which the review question raised: the creator floor is `manage` on that event (rule 6), the people concerned only get back what the calendar's managers gave them, and the alternative (manager entries a creator cannot touch) needs per-entry ownership the model does not have; calendars that need it disable the creator floor. The only removal that can move data *outside* the audience — exposing an outsider to an external group share — is now refused without external rights. |
| 10 | Principal selection: own groups, the owner group, granted groups; else `422`. | Confirmed. Not re-checked on moves (the group was selectable where the override was set; only the external rule decides about data leaving the audience). |
| 11 | Moves re-validated as if the mover set the overrides in T (external rights in T). | **Amended (P2)**: no floor in T → no override travels. |
| — | Exceptions' overrides are the series'; self-lockout asserted (debug + property). | Confirmed. |

## 4. Open product questions

1. **Restricting owners and managers** (family surprise row): managers and owners always see everything (rule 5), so a family member cannot hide a surprise party from the parent who owns the calendar. Is "use your personal calendar and share the one event" good enough, or do we want a per-event "hide from managers" that keeps managers able to *delete* but not *read*?
2. **Demoted creators** (club row): after a demotion below `contribute`, the creator's own `everyone → none` hides the event from them. Should a former creator keep at least `read` on their own events while they still reach the calendar?
3. **Manager-locked restrictions**: creators can lift a manager's restriction on their own events (decision 9). Clubs/companies that want "a manager decided this, creators cannot undo it" today must disable the creator floor for the whole calendar. Is a per-override lock worth the extra rule?

## 5. Verification

`dotnet build backend -c Release` (0 warnings), `dotnet format backend --verify-no-changes`, `dotnet test --solution backend` (801 tests; 714 before the review; Core 408: 78 scenario cases, 5 new property tests, 4 hot-path tests), `python3 backend/scripts/engine-coverage.py` (100 % lines and branches: 392/392, 299/299), markdownlint-cli2.
