import { ApiError } from './apiError'

/**
 * `402` answers of the api (docs/architecture/api.md §2): `plan_limit_reached` with
 * `limit: { key, max, used }`, `plan` and `trialEndsAt` (set while `plan` is the reverse trial's
 * `pro`), and `feature_not_in_plan` with `feature`. The plan is the one of the resource's owner
 * (docs/product/plans.md principle 2), not necessarily mine.
 */
export type PlanProblem =
  | {
      kind: 'limit'
      /** `owned_calendars`, `owned_groups`, `members_per_group`, `events_with_overrides`, … */
      key: string
      max: number | null
      used: number | null
      plan: string | null
      trialEndsAt: string | null
    }
  | { kind: 'feature'; feature: string; plan: string | null; trialEndsAt: string | null }

/** Limit keys with their own wording (others get the generic one). */
export const knownLimitKeys = [
  'owned_calendars',
  'owned_groups',
  'members_per_group',
  'events_with_overrides',
  'overrides_per_event',
] as const
export type KnownLimitKey = (typeof knownLimitKeys)[number]

export const isKnownLimitKey = (key: string): key is KnownLimitKey =>
  (knownLimitKeys as readonly string[]).includes(key)

/** Plans with their own wording. */
export type PlanName = 'free' | 'pro' | 'team' | 'selfhost'

const count = (value: unknown) =>
  typeof value === 'number' && Number.isInteger(value) && value >= 0 ? value : null
const text = (value: unknown) => (typeof value === 'string' && value !== '' ? value : null)

/** The plan details of a `402`, or null for any other error. */
export function planProblemOf(error: unknown): PlanProblem | null {
  if (!(error instanceof ApiError) || error.status !== 402) return null
  const body = error.problem as Record<string, unknown>
  const plan = text(body.plan)
  const trialEndsAt = text(body.trialEndsAt)
  if (error.code === 'feature_not_in_plan') {
    return { kind: 'feature', feature: text(body.feature) ?? 'unknown', plan, trialEndsAt }
  }
  if (error.code !== 'plan_limit_reached') return null
  const limit = (typeof body.limit === 'object' && body.limit !== null ? body.limit : {}) as Record<
    string,
    unknown
  >
  return {
    kind: 'limit',
    key: text(limit.key) ?? 'unknown',
    max: count(limit.max),
    used: count(limit.used),
    plan,
    trialEndsAt,
  }
}

/** Which plan wording applies: the reverse trial is a `pro` plan with `trialEndsAt`. */
export function planKind(
  problem: Pick<PlanProblem, 'plan' | 'trialEndsAt'>,
): 'free' | 'trial' | 'pro' | 'team' | 'selfhost' | 'unknown' {
  switch (problem.plan) {
    case 'free':
      return 'free'
    case 'pro':
      return problem.trialEndsAt ? 'trial' : 'pro'
    case 'team':
      return 'team'
    case 'selfhost':
      return 'selfhost'
    default:
      return 'unknown'
  }
}

/** "Not now" is remembered per limit key or feature. */
export function suppressionKey(problem: PlanProblem): string {
  return problem.kind === 'limit' ? `limit:${problem.key}` : `feature:${problem.feature}`
}

/** "Not now" hides the same dialog for 14 days (docs/product/plans.md "Upgrade triggers"). */
export const notNowDays = 14
const storageKey = 'scal.paywall.notNow'
const day = 24 * 60 * 60 * 1000

function readSuppressed(): Record<string, number> {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(storageKey) ?? '{}')
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return {}
    return Object.fromEntries(
      Object.entries(parsed).filter((entry): entry is [string, number] =>
        Number.isFinite(entry[1]),
      ),
    )
  } catch {
    return {}
  }
}

export function isSuppressed(key: string, now = Date.now()): boolean {
  const until = readSuppressed()[key]
  return until !== undefined && until > now
}

export function suppress(key: string, now = Date.now()): void {
  // Expired entries are dropped on the way.
  const entries = Object.entries(readSuppressed()).filter(([, until]) => until > now)
  try {
    localStorage.setItem(
      storageKey,
      JSON.stringify(Object.fromEntries([...entries, [key, now + notNowDays * day]])),
    )
  } catch {
    // Storage blocked: the dialog shows again next time.
  }
}
