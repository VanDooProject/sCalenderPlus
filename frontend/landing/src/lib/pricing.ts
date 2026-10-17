import type { LimitKey, Plan, PlanCatalogue, PlanLimits } from '@scalenderplus/plans'
import type { Locale } from './routes'

/** Rows of the comparison table, in order (labels: `pricing.limits.<key>`). */
export const limitRows: readonly LimitKey[] = [
  'ownedCalendars',
  'ownedGroups',
  'membersPerGroup',
  'eventsWithOverrides',
  'overridesPerEvent',
  'importSources',
  'importInterval',
  'llmRunsPerMonth',
  'autoPublish',
  'historyDays',
  'apiTokens',
  'webhooks',
  'support',
]

/** How a cell reads, before translation: a number, unlimited, yes/no or a message key. */
export type Cell =
  | { kind: 'number'; value: number }
  | { kind: 'unlimited' }
  | { kind: 'included' }
  | { kind: 'notIncluded' }
  | { kind: 'days'; value: number }
  | { kind: 'years'; value: number }
  | { kind: 'message'; key: string }

export function cell<K extends LimitKey>(key: K, value: PlanLimits[K]): Cell {
  if (value === null) return { kind: 'unlimited' }
  if (typeof value === 'boolean') return { kind: value ? 'included' : 'notIncluded' }
  if (key === 'importInterval') return { kind: 'message', key: `pricing.interval.${value}` }
  if (key === 'support') return { kind: 'message', key: `pricing.supportLevel.${value}` }
  if (typeof value !== 'number') throw new Error(`Unexpected value of ${key}: ${String(value)}`)
  if (value === 0) return { kind: 'notIncluded' }
  if (key === 'historyDays') {
    return value % 365 === 0 ? { kind: 'years', value: value / 365 } : { kind: 'days', value }
  }
  return { kind: 'number', value }
}

/** Rows about the website importer (left out by editions that don't market it). */
export const importerRows: readonly LimitKey[] = [
  'importSources',
  'importInterval',
  'llmRunsPerMonth',
  'autoPublish',
]

/** The rows an edition shows. */
export const visibleRows = (importer: boolean) =>
  limitRows.filter((key) => importer || !importerRows.includes(key))

export function formatNumber(locale: Locale, value: number): string {
  return new Intl.NumberFormat(locale).format(value)
}

export function formatPrice(locale: Locale, currency: string, amount: number): string {
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency,
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
    maximumFractionDigits: 2,
  }).format(amount)
}

/** The plan marked as popular. */
export const recommendedPlan = (catalogue: PlanCatalogue): Plan | undefined =>
  catalogue.plans.find((plan) => plan.id === 'pro')
