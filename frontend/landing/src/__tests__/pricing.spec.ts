import { describe, expect, it } from 'vitest'
import { catalogue } from '@scalenderplus/plans'
import { cell, formatPrice, importerRows, limitRows, visibleRows } from '@/lib/pricing'

describe('pricing table', () => {
  it('has a row for every limit of plans.json', () => {
    expect([...limitRows].sort()).toEqual(Object.keys(catalogue.plans[0]!.limits).sort())
  })

  it('leaves out the importer rows for editions without the importer', () => {
    expect(visibleRows(true)).toEqual(limitRows)
    const rows = visibleRows(false)
    for (const row of importerRows) expect(rows).not.toContain(row)
    expect(rows).toContain('eventsWithOverrides')
  })

  it.each([
    ['ownedCalendars', 3, { kind: 'number', value: 3 }],
    ['eventsWithOverrides', null, { kind: 'unlimited' }],
    ['autoPublish', false, { kind: 'notIncluded' }],
    ['autoPublish', true, { kind: 'included' }],
    ['webhooks', 0, { kind: 'notIncluded' }],
    ['historyDays', 7, { kind: 'days', value: 7 }],
    ['historyDays', 1095, { kind: 'years', value: 3 }],
    ['importInterval', 'daily', { kind: 'message', key: 'pricing.interval.daily' }],
    ['support', 'email', { kind: 'message', key: 'pricing.supportLevel.email' }],
  ] as const)('shows %s = %o as %o', (key, value, expected) => {
    expect(cell(key, value as never)).toEqual(expected)
  })

  it('formats prices per locale', () => {
    expect(formatPrice('en', 'EUR', 12)).toBe('€12')
    expect(formatPrice('de', 'EUR', 120)).toBe('120 €')
    expect(formatPrice('en', 'EUR', 9.5)).toBe('€9.50')
  })
})
