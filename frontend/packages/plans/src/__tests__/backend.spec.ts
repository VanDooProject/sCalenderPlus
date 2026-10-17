import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { catalogue, withOverrides, type LimitKey, type PlanId } from '../index'

/**
 * plans.json is the landing's copy of the plan numbers; the api's are the defaults of `PlansOptions`.
 * This reads the C# source (no .NET needed in the frontend job) so the two cannot drift: a changed
 * default, a new plan limit or a new trial length fails here until plans.json follows.
 */
const optionsFile = fileURLToPath(
  new URL(
    '../../../../../backend/src/SCalenderPlus.Application/Entitlements/EntitlementOptions.cs',
    import.meta.url,
  ),
)
const source = readFileSync(optionsFile, 'utf8')

const camel = (name: string) => name.charAt(0).toLowerCase() + name.slice(1)

/** `public int? OwnedCalendars { get; set; }` of `PlanLimitOptions`, in declaration order. */
function limitProperties(): string[] {
  const body = /public sealed class PlanLimitOptions\s*\{([\s\S]*?)\n\}/.exec(source)?.[1]
  if (body === undefined) throw new Error('PlanLimitOptions not found in EntitlementOptions.cs')
  return [...body.matchAll(/public int\? (\w+) \{ get; set; \}/g)].map((m) => camel(m[1]!))
}

/** `public PlanLimitOptions Free { get; set; } = new() { OwnedCalendars = 3, … };` per plan. */
function planDefaults(): Map<string, Map<string, number>> {
  const plans = new Map<string, Map<string, number>>()
  const pattern = /public PlanLimitOptions (\w+) \{ get; set; \} = new\(\)\s*(?:\{([^}]*)\})?\s*;/g
  for (const [, plan, initializer] of source.matchAll(pattern)) {
    const values = new Map<string, number>()
    for (const assignment of (initializer ?? '').split(',')) {
      if (assignment.trim() === '') continue
      const match = /^\s*(\w+)\s*=\s*(\d+)\s*$/.exec(assignment)
      if (match === null) throw new Error(`Unexpected initializer of ${plan}: "${assignment}"`)
      values.set(camel(match[1]!), Number(match[2]))
    }
    plans.set(camel(plan!), values)
  }
  return plans
}

describe('plans.json matches the backend defaults (PlansOptions)', () => {
  it('lists exactly the limits the backend has', () => {
    const properties = limitProperties()
    expect(properties.length).toBeGreaterThanOrEqual(5)
    expect([...catalogue.backendLimits].sort()).toEqual([...properties].sort())
  })

  it('has the backend default of every enforced limit (missing = unlimited)', () => {
    const defaults = planDefaults()
    expect([...defaults.keys()].sort()).toEqual(['free', 'pro', 'selfHost', 'team'])
    for (const plan of catalogue.plans) {
      const backend = defaults.get(plan.id)
      expect(backend, plan.id).toBeDefined()
      for (const key of catalogue.backendLimits) {
        expect(plan.limits[key], `${plan.id}.${key}`).toBe(backend!.get(key) ?? null)
      }
      for (const key of backend!.keys()) {
        expect(catalogue.backendLimits, `${plan.id}: ${key} is set in C#`).toContain(key)
      }
    }
    // Self-host is unlimited by default and not on the pricing table.
    expect(defaults.get('selfHost')!.size).toBe(0)
  })

  it('has the backend default trial length', () => {
    const trialDays = /public int TrialDays \{ get; set; \} = (\d+);/.exec(source)?.[1]
    expect(trialDays).toBeDefined()
    expect(catalogue.trialDays).toBe(Number(trialDays))
  })
})

describe('plans.json', () => {
  it('lists free, pro and team with the same limit keys', () => {
    expect(catalogue.plans.map((p) => p.id)).toEqual(['free', 'pro', 'team'])
    const keys = Object.keys(catalogue.plans[0]!.limits).sort()
    for (const plan of catalogue.plans) expect(Object.keys(plan.limits).sort()).toEqual(keys)
  })

  it('has the prices of plans.md (annual = 10 months)', () => {
    const price = (id: PlanId) => catalogue.plans.find((p) => p.id === id)!.price
    expect(catalogue.currency).toBe('EUR')
    expect(price('free')).toMatchObject({ monthly: 0, yearly: 0 })
    expect(price('pro')).toMatchObject({ monthly: 12, yearly: 120, perSeat: false })
    expect(price('team')).toMatchObject({ monthly: 8, yearly: 80, perSeat: true, minSeats: 3 })
  })
})

describe('withOverrides', () => {
  it('replaces single limits', () => {
    const changed = withOverrides(catalogue, { free: { eventsWithOverrides: 50 } })
    expect(changed.plans[0]!.limits.eventsWithOverrides).toBe(50)
    expect(changed.plans[0]!.limits.ownedCalendars).toBe(3)
    expect(catalogue.plans[0]!.limits.eventsWithOverrides).toBe(10)
  })

  it('rejects unknown plans and limits', () => {
    expect(() => withOverrides(catalogue, { gold: {} } as never)).toThrow(/Unknown plan/)
    expect(() =>
      withOverrides(catalogue, {
        free: { seats: 1 } as unknown as Partial<Record<LimitKey, never>>,
      }),
    ).toThrow(/Unknown limit/)
  })
})
