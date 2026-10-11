type Translate = (key: string, named?: Record<string, unknown>) => string

/**
 * A short read-only description of an RRULE ("Weekly, 8 times"); the recurrence editor is #59.
 * Parts it does not describe are left out; an unknown frequency shows the rule itself.
 */
export function describeRule(t: Translate, rrule: string): string {
  const parts = Object.fromEntries(
    rrule
      .replace(/^RRULE:/i, '')
      .split(';')
      .map((p) => p.split('=') as [string, string])
      .map(([k, v]) => [k.toUpperCase(), v ?? '']),
  )
  const freq = (parts.FREQ ?? '').toLowerCase()
  if (!['daily', 'weekly', 'monthly', 'yearly'].includes(freq)) return rrule
  const interval = Number(parts.INTERVAL ?? '1')
  const base =
    interval > 1 ? t(`event.rule.every.${freq}`, { n: interval }) : t(`event.rule.${freq}`)
  if (parts.COUNT) return t('event.rule.count', { rule: base, n: Number(parts.COUNT) })
  if (parts.UNTIL) {
    const u = parts.UNTIL
    const date = `${u.slice(0, 4)}-${u.slice(4, 6)}-${u.slice(6, 8)}`
    return t('event.rule.until', { rule: base, date })
  }
  return base
}
