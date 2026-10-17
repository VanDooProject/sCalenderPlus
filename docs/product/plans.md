# Plans, Limits and Pricing

## Principles

1. **Reverse trial on top of a Free plan that stays useful forever.** Every new account gets the **Pro plan's limits for 90 days** from its creation (`users.created_at`), then falls back to Free under the normal downgrade rules (over-limit stays, only growth is refused; nothing deleted, overrides stay enforced). People experience the core feature at full strength before they meet a limit. Free itself remains a complete product for a family or a small club: we gate **power** — scale, automation, fine-grained control, retention, integrations — never basic usefulness. Configured with `Plans__TrialDays` (default `90`, `0` disables, at most `3650`; other values fail start-up) and only active with billing on (`Billing__Provider=stripe`); self-host stays unlimited. The resource owner governs (principle 2): a trialing owner's group calendars have Pro limits for everyone in them. For a group's resources (its calendars, members and the overrides in its calendars) the trial counts from the earlier of the billing owner's and the group's creation (`groups.created_at`), so transferring billing to a new account never restarts it; personal resources count from the owner's account alone. **Decision:** the trial raises the calendar, group, member and override limits only; the LLM import quotas (sources, minimum interval, LLM runs per month) stay at the Free values during the trial, so new accounts can't be farmed for Pro LLM runs. LLM quotas switch to Pro values with a paid Pro plan.
2. **The resource owner's plan governs.** Limits and features on a calendar are determined by the plan of the calendar's owner (user; for group-owned calendars the group's billing owner `groups.owner_user_id`, or the organization owning the group). A free user inside a Pro-owned group calendar enjoys Pro features *in that calendar*. This makes one paying organizer enough for a whole club — the right buyer.
3. **Downgrades never delete and never leak.** Over-limit resources are frozen (read-only), not removed. Existing permission overrides **stay enforced**; only creating new ones is blocked.
4. **Limits are visible.** Every gated resource shows a usage meter; hitting a limit shows what upgrading unlocks.
5. **Server-side enforcement only** through one `IEntitlementService`; the UI merely reflects it.

## Tiers

| | **Free** | **Pro** | **Team** | **Self-host** |
|---|---|---|---|---|
| Target | Families, tiny clubs, trying it out | Organizers of clubs/communities, power users | Companies, larger associations | Own infrastructure |
| Billing subject | user | user | organization | – |
| Price (EUR, excl. VAT) | €0 | **€12 / month** or **€120 / year** | **€8 / seat / month** or €80 / seat / year, min. 3 seats | €0 (support contracts optional) |

### Seat definition (Team)

A seat is a distinct user who can **create or change content** in any org-owned calendar — effective `Lc ≥ contribute` via role default *or* direct grant, or holding an `edit` override on an org event (otherwise individual grants/overrides to viewers would bypass seats). **Read-only people are free** up to 10 × seats; beyond that each further 10 read-only people count as one seat. Clubs with 200 members and 8 organizers pay for 8 seats. This is a deliberate differentiator against per-user pricing.

## Limits

| Limit / feature | Free | Pro | Team (per organization) | Why this number |
|---|---|---|---|---|
| Owned calendars | 3 | 30 | 300 | 3 covers personal + family + one club; power users split by topic. |
| Owned groups | 1 | 10 | unlimited | One club/family free; organizers of several groups pay. |
| Members per owned group | 15 | 150 | 1,000 | 15 = family or small team; a typical club section exceeds it → natural upgrade trigger. |
| **Editors** (distinct users with `Lc ≥ contribute` across all owned calendars) | 15 | 25 | = seats | *Proposed.* Stops one €12 Pro account from running a many-organizer organisation (10 groups × 150 members) that should be on Team; read-only audiences stay unlimited. |
| Group roles | fixed 4 | fixed 4 | + custom roles (later) | |
| **Events with permission overrides** (active, i.e. not ended, per owner) | **10** | unlimited | unlimited | Lets users *experience* the core feature weekly but not run a whole calendar on it. Past events don't count, so the limit doesn't punish history. |
| Override entries per event | 3 | 25 | 100 | Free covers "hide from everyone except X"; complex ACLs are power use. |
| `contribute` level & group-role defaults | ✓ | ✓ | ✓ | Core club workflow; gating it would kill adoption. |
| Share links | 1 per calendar (read or free/busy) | unlimited, with expiry | same as Pro | Gating the *more* private free/busy option made no sense. |
| Public embeddable widget | 1 calendar, with "Powered by" | unlimited, no branding | unlimited | Free widget = acquisition loop. |
| Personalised iCal feeds | ✓ unlimited | ✓ | ✓ | Native integration is the hook — never gate it. |
| Custom feed labels (own text/emoji, per-feed mode) | default labels only | ✓ | ✓ | |
| CalDAV (later) | read-only | read/write | read/write | Two-way sync is a classic paid power feature. |
| **LLM import sources** | 1 | 10 | 50 | One school or league website free. |
| Minimum import interval | weekly | daily | hourly | Frequency drives cost. |
| LLM-processed runs / month (unchanged pages are free) | 8 | 300 | 2,000 (pooled) | Hard cost cap; ~€0.01–0.05 per run → Free ≤ €0.40/user/month worst case. |
| Max events extracted per run | 25 | 200 | 500 | |
| Auto-publish (skip review queue) | ✗ review always | ✓ | ✓ | Review on Free also limits abuse. |
| ICS subscription import (external .ics URL) | 1, every 24 h | 20, every 1 h | 100, every 15 min | No LLM cost but load. |
| Event history / audit log retention | 7 days | 1 year | 3 years | Retention is cheap to gate and valued by orgs. |
| Restore previous event version (later, M8) | ✗ | ✓ | ✓ | Not sold at paid launch. |
| Reminders | email + web push | same | same | Retention driver; keep free. |
| Email digests | weekly | daily/weekly | daily/weekly | |
| Personal API tokens | 1 (read-only scope) | 10 | 100 | Free can try the API. |
| Webhooks | ✗ | 5 endpoints | 50 endpoints | Integrations = power. |
| API rate limit (bearer tokens; web app sessions exempt, see api.md) | 60 req/min | 600 req/min | 1,200 req/min per org | |
| Attachments (later) | ✗ | 1 GB | 5 GB + 1 GB/seat | |
| Admin console, org-wide audit export | ✗ | ✗ | ✓ | |
| SSO (OIDC/SAML, later) | ✗ | ✗ | ✓ | |
| Support | community | email, 2 business days | email, 1 business day | |

All numbers live in a single configuration, so marketing can tune them without a release: `PlansOptions` (`Application/Entitlements`) carries the numbers of this table as defaults, and every limit can be overridden per plan with `Plans__{Plan}__{Limit}` (empty = unlimited). Enforced so far: owned calendars, owned groups, members per group, events with overrides, override entries per event, and the reverse trial (Pro limits until `trialEndsAt`). `GET /api/v1/me/entitlements` returns `{ plan, trialEndsAt, limits }` (`null` = unlimited; [api.md](../architecture/api.md#me-implemented-m1)); usage counts follow in M5. A DB table `plan_limits` may replace the configuration with billing.

**Marketing copy of the numbers:** [`frontend/packages/plans/plans.json`](../../frontend/packages/plans/plans.json) holds the prices and limits of this page as data; the landing's pricing table is generated from it. Its unit test reads the defaults of `PlansOptions` from `EntitlementOptions.cs` and fails when an enforced limit, the set of enforced limits or `TrialDays` differs, so a changed backend default can't silently diverge from the published prices page (the CI `frontend` job runs when that file changes). The not yet enforced limits (imports, history, API tokens, …) are checked by review against this page. A deployment that overrides `Plans__*` builds its landing with the same numbers (edition overrides in `frontend/landing/src/editions/<edition>/index.ts`).

## Per-edition defaults

Each edition is an independent deployment ([editions.md](editions.md)) and tunes limits through its own `Plans__*` environment; prices and Stripe products are per deployment too.

| Limit | Standard Free | Poly Free (*proposal*) | Why |
|---|---|---|---|
| Events with permission overrides | 10 | unlimited or 50 | In the poly use cases nearly every event is shared selectively (date with one partner, busy for the rest); 10 would block normal use. |
| Override entries per event | 3 | 10 | Partner + metamours + constellation per event. |
| Members per owned group | 15 | 15 | Constellations are small; same as standard. |
| Trial | `Plans__TrialDays=90` | same | |

The poly landing advertises 50 and 10 (its plan overrides), so the poly deployment must set the same values. Set via `Plans__Free__EventsWithOverrides`, `Plans__Free__OverridesPerEvent`, `Plans__Free__MembersPerGroup` in the poly deployment ([coolify.md §4](../deployment/coolify.md#4-environment-variables)).

## Upgrade triggers (where the paywall appears)

| Moment | Message |
|---|---|
| Creating the 11th active event with an override | "Private and locked entries are Pro's superpower — unlimited with Pro." |
| Inviting the 16th member into a group | "Your group is growing. Pro supports up to 150 members per group." |
| Creating a 2nd import source or choosing daily schedule | "Keep calendars filled automatically: 10 sources, daily, auto-publish." |
| Opening history older than 7 days | "See a year of changes with Pro." |
| Adding a webhook / 2nd API token | "Connect sCalenderPlus to your tools with Pro." |
| Org with > 1 Pro user in same group | Suggest Team (central billing, seats, admin console). |
| Trial ends in 7 days (*planned*: email + in-app banner) | "Your Pro trial ends on {date}. Keep unlimited private entries and 150 members per group with Pro." |

Paywalls appear **in context, after the user tried to do something**, with a one-click upgrade and a "not now" that never nags again for 14 days. Implemented in the web app for the enforced limits ([overview.md §4.1](../architecture/overview.md#41-web-app-structure-implemented-m3)): the dialog shows the limit's numbers and the governing plan's wording from the `402` (Free, Pro trial with its end, paid Pro, Team, self-hosted); until billing exists (M5) it says "coming soon" instead of offering a checkout. Its messages avoid fixed Pro numbers, since every limit is configurable per deployment.

## Downgrade and payment-failure behaviour

| Situation | Behaviour |
|---|---|
| Over calendar limit | Excess (newest first) become **frozen**: visible, feeds keep working; no create/edit/move-in, deletes and override *removal* allowed (so owners can clean up). Owner chooses which to keep active. |
| Over group limit | Excess groups frozen: membership unchanged, no invites/role changes; their calendars follow the calendar rule. |
| Overrides over limit (count or entries/event) | Existing overrides remain **enforced and editable only to remove**; no new overrides. |
| Members / editors over limit | Existing stay; no new invites or editor grants. |
| Import sources | Over-limit sources paused; schedule clamped to plan minimum; auto-publish → review. |
| Share links, API tokens, webhooks over limit | Existing share links keep working (revoking is the owner's choice); newest over-limit API tokens and all webhooks (Free) disabled, not deleted. |
| Custom feed labels / CalDAV write | Revert to default labels / read-only. |
| Audit/history | Retention job trims to new retention after 30-day grace. |
| Payment failed | Stripe dunning (3 retries over 14 days) → then downgrade as above. |
| Ownership transfer to a lower-plan subject | Same rules, applied immediately; UI warns before the transfer. |
| Reverse trial ended | A downgrade from Pro limits to Free: same rules, applied at `trialEndsAt` without a job (entitlements are computed from `created_at`). *Planned:* reminder email and in-app banner 7 days before. |

**Shared-quota note:** on a Free group calendar every contributor's overrides count against the billing owner's 10. A single member can exhaust it; the usage meter shows usage per creator and managers can remove overrides. Accepted for MVP; revisit with beta data.

## Billing architecture (summary)

- `IBillingProvider` abstraction: `CreateCheckoutSession`, `CreatePortalSession`, `HandleWebhook`, `GetSubscription`.
- Implementations: `StripeBillingProvider` (default for SaaS), `NoBillingProvider` (self-host: every subject resolves to plan `selfhost`).
- Stripe is the source of truth for subscriptions; we mirror state in `subscriptions` via webhooks (idempotent by event id). Entitlements are computed from the mirrored state, never by calling Stripe on the request path.
- Stripe Tax for EU VAT, Stripe Checkout and Customer Portal (no card forms of our own → minimal PCI scope).
- Configuration: `Billing__Provider=stripe|none`, `Billing__Stripe__SecretKey`, `Billing__Stripe__WebhookSecret`, price ids per plan/interval.

## Self-host stance

- The full product is self-hostable with `Billing__Provider=none` (the default); plan `selfhost` defaults to **unlimited** for all limits. Operators can override any limit via `Plans__SelfHost__*` env vars (e.g. cap LLM runs because *they* pay the LLM bill).
- No license-key checks or phone-home in the code. LLM import requires the operator's own API key (or an OpenAI-compatible local endpoint).
- Proposed license: **AGPL-3.0** for the code (protects the SaaS from closed forks while allowing self-hosting); commercial license available on request. *This is an open owner decision* (see roadmap open questions).
- The marketing site and brand assets are not part of the self-host distribution.

## Pricing rationale

- **Pro at €12/month** is deliberately "pricey" vs. consumer calendars but cheap vs. Teamup's higher tiers and per-seat tools; the buyer is one organizer serving a whole group. Annual = 2 months free.
- **Team per seat (creators only)** scales with value for companies while staying affordable for associations with many passive members.
- Free costs us ≈ storage + feed bandwidth + ≤ 8 LLM runs/month; feed responses are cached (ETag), so the marginal Free user is < €0.10/month. **Trial users** cost about the same as Free users: the trial keeps the Free LLM quotas (≤ 8 runs/month, principle 1), and unpaid LLM usage counts against the global daily Free LLM budget ([llm-import.md §8](../architecture/llm-import.md#8-cost-controls)), so account farming gains no LLM runs.
- Non-profit discount (50 % on Team) is recommended for registered associations — strong word-of-mouth in the club segment.
