# Plans, Limits and Pricing

## Principles

1. **Free forever, no trial clock.** Free is a complete product for a family or a small club. We gate **power** — scale, automation, fine-grained control, retention, integrations — never basic usefulness.
2. **The resource owner's plan governs.** Limits and features on a calendar are determined by the plan of the calendar's owner (user, or the organization owning the group). A free user inside a Pro-owned group calendar enjoys Pro features *in that calendar*. This makes one paying organizer enough for a whole club — the right buyer.
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

A seat is an organization member with role ≥ `member` in any organization group (people who can create content). **Viewers are free** up to 10 × seats. Clubs with 200 members and 8 organizers pay for 8 seats. This is a deliberate differentiator against per-user pricing.

## Limits

| Limit / feature | Free | Pro | Team (per organization) | Why this number |
|---|---|---|---|---|
| Owned calendars | 3 | 30 | 300 | 3 covers personal + family + one club; power users split by topic. |
| Owned groups | 1 | 10 | unlimited | One club/family free; organizers of several groups pay. |
| Members per owned group | 15 | 150 | 1,000 | 15 = family or small team; a typical club section exceeds it → natural upgrade trigger. |
| Group roles | fixed 4 | fixed 4 | + custom roles (later) | |
| **Events with permission overrides** (active, i.e. not ended, per owner) | **10** | unlimited | unlimited | Lets users *experience* the core feature weekly but not run a whole calendar on it. Past events don't count, so the limit doesn't punish history. |
| Override entries per event | 3 | 25 | 100 | Free covers "hide from everyone except X"; complex ACLs are power use. |
| `contribute` level & group-role defaults | ✓ | ✓ | ✓ | Core club workflow; gating it would kill adoption. |
| Share links | 1 read-only per calendar | unlimited, expiry, free/busy or read | same as Pro | |
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
| Restore previous event version | ✗ | ✓ | ✓ | |
| Reminders | email + web push | same | same | Retention driver; keep free. |
| Email digests | weekly | daily/weekly | daily/weekly | |
| Personal API tokens | 1 (read-only scope) | 10 | 100 | Free can try the API. |
| Webhooks | ✗ | 5 endpoints | 50 endpoints | Integrations = power. |
| API rate limit | 60 req/min | 600 req/min | 1,200 req/min per org | |
| Attachments (later) | ✗ | 1 GB | 5 GB + 1 GB/seat | |
| Admin console, org-wide audit export | ✗ | ✗ | ✓ | |
| SSO (OIDC/SAML, later) | ✗ | ✗ | ✓ | |
| Support | community | email, 2 business days | email, 1 business day | |

All numbers live in a single configuration (`plans.json` / DB table `plan_limits`), not in code, so marketing can tune them without a release.

## Upgrade triggers (where the paywall appears)

| Moment | Message |
|---|---|
| Creating the 11th active event with an override | "Private and locked entries are Pro's superpower — unlimited with Pro." |
| Inviting the 16th member into a group | "Your group is growing. Pro supports up to 150 members per group." |
| Creating a 2nd import source or choosing daily schedule | "Keep calendars filled automatically: 10 sources, daily, auto-publish." |
| Opening history older than 7 days | "See and restore a year of changes with Pro." |
| Adding a webhook / 2nd API token | "Connect sCalenderPlus to your tools with Pro." |
| Org with > 1 Pro user in same group | Suggest Team (central billing, seats, admin console). |

Paywalls appear **in context, after the user tried to do something**, with a one-click upgrade and a "not now" that never nags again for 14 days.

## Downgrade and payment-failure behaviour

| Situation | Behaviour |
|---|---|
| Over calendar/group limits | Excess (newest first) become **frozen**: visible, read-only, feeds keep working. Owner chooses which to keep active. |
| Overrides over limit | Existing overrides remain **enforced and editable only to remove**; no new overrides. |
| Members over limit | Existing members stay; no new invites. |
| Import sources | Over-limit sources paused; schedule clamped to plan minimum. |
| Audit/history | Retention job trims to new retention after 30-day grace. |
| Payment failed | Stripe dunning (3 retries over 14 days) → then downgrade as above. |

## Billing architecture (summary)

- `IBillingProvider` abstraction: `CreateCheckoutSession`, `CreatePortalSession`, `HandleWebhook`, `GetSubscription`.
- Implementations: `StripeBillingProvider` (default for SaaS), `NoBillingProvider` (self-host: every subject resolves to plan `selfhost`).
- Stripe is the source of truth for subscriptions; we mirror state in `subscriptions` via webhooks (idempotent by event id). Entitlements are computed from the mirrored state, never by calling Stripe on the request path.
- Stripe Tax for EU VAT, Stripe Checkout and Customer Portal (no card forms of our own → minimal PCI scope).
- Configuration: `Billing__Provider=stripe|none`, `Billing__Stripe__SecretKey`, `Billing__Stripe__WebhookSecret`, price ids per plan/interval.

## Self-host stance

- The full product is self-hostable with `Billing__Provider=none`; plan `selfhost` defaults to **unlimited** for all limits. Operators can override any limit via `Plans__SelfHost__*` env vars (e.g. cap LLM runs because *they* pay the LLM bill).
- No license-key checks or phone-home in the code. LLM import requires the operator's own API key (or an OpenAI-compatible local endpoint).
- Proposed license: **AGPL-3.0** for the code (protects the SaaS from closed forks while allowing self-hosting); commercial license available on request. *This is an open owner decision* (see roadmap open questions).
- The marketing site and brand assets are not part of the self-host distribution.

## Pricing rationale

- **Pro at €12/month** is deliberately "pricey" vs. consumer calendars but cheap vs. Teamup's higher tiers and per-seat tools; the buyer is one organizer serving a whole group. Annual = 2 months free.
- **Team per seat (creators only)** scales with value for companies while staying affordable for associations with many passive members.
- Free costs us ≈ storage + feed bandwidth + ≤ 8 LLM runs/month; feed responses are cached (ETag), so the marginal Free user is < €0.10/month.
- Non-profit discount (50 % on Team) is recommended for registered associations — strong word-of-mouth in the club segment.
