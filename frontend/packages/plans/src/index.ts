import data from '../plans.json'

/**
 * Plan prices and limits of docs/product/plans.md as data (`plans.json`): the one source of the
 * landing's pricing table. The limits the api enforces (`backendLimits`) are checked against the
 * defaults of the backend's `PlansOptions` in this package's tests. A deployment may still override
 * any limit with `Plans__*` (marketing then rebuilds the landing with the same overrides).
 */
export type PlanId = 'free' | 'pro' | 'team'

/** Shortest schedule an import source may run on. */
export type ImportInterval = 'weekly' | 'daily' | 'hourly'
export type SupportLevel = 'community' | 'email' | 'priority'

export interface PlanLimits {
  ownedCalendars: number | null
  ownedGroups: number | null
  membersPerGroup: number | null
  eventsWithOverrides: number | null
  overridesPerEvent: number | null
  importSources: number | null
  importInterval: ImportInterval
  llmRunsPerMonth: number | null
  /** Event history / audit log retention. */
  historyDays: number | null
  apiTokens: number | null
  /** 0 = not included. */
  webhooks: number | null
  autoPublish: boolean
  support: SupportLevel
}

export type LimitKey = keyof PlanLimits

export interface PlanPrice {
  /** Excl. VAT, in `currency`; per seat when `perSeat`. */
  monthly: number
  yearly: number
  perSeat: boolean
  minSeats: number | null
}

export interface Plan {
  id: PlanId
  price: PlanPrice
  limits: PlanLimits
}

export interface PlanCatalogue {
  currency: string
  /** Reverse trial: Pro limits for this many days after sign-up (`Plans__TrialDays`). */
  trialDays: number
  /** Limits the api enforces today (`PlansOptions`), camelCased. */
  backendLimits: LimitKey[]
  plans: Plan[]
}

/** Limits that differ from the defaults, per plan (e.g. an edition's more generous Free plan). */
export type PlanOverrides = Partial<Record<PlanId, Partial<PlanLimits>>>

export const catalogue = data as PlanCatalogue

/** The catalogue with some limits replaced (unknown plans or limits are an error, not ignored). */
export function withOverrides(base: PlanCatalogue, overrides: PlanOverrides): PlanCatalogue {
  const ids = new Set(base.plans.map((plan) => plan.id))
  for (const [id, limits] of Object.entries(overrides)) {
    if (!ids.has(id as PlanId)) throw new Error(`Unknown plan "${id}" in plan overrides`)
    for (const key of Object.keys(limits ?? {})) {
      if (!(key in base.plans[0]!.limits)) throw new Error(`Unknown limit "${key}" of plan "${id}"`)
    }
  }
  return {
    ...base,
    plans: base.plans.map((plan) => ({
      ...plan,
      limits: { ...plan.limits, ...overrides[plan.id] },
    })),
  }
}
