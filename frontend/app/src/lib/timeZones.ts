/** IANA zone of this browser (the default of new accounts), falling back to UTC. */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  } catch {
    return 'UTC'
  }
}

let cached: string[] | null = null

/** IANA zones this browser knows (canonical ids), always including UTC. */
export function availableTimeZones(): string[] {
  if (!cached) {
    const zones =
      typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []
    cached = Array.from(new Set([...zones, 'UTC'])).sort()
  }
  return cached
}

/** The zone's current UTC offset, e.g. `GMT+2` (as the browser formats it), or empty. */
export function timeZoneOffset(zone: string, at: Date = new Date()): string {
  try {
    const part = new Intl.DateTimeFormat('en', { timeZone: zone, timeZoneName: 'shortOffset' })
      .formatToParts(at)
      .find((p) => p.type === 'timeZoneName')
    return part?.value ?? ''
  } catch {
    return ''
  }
}

/** Case-insensitive match on the id with `_`/`/` treated as spaces (`new york`, `berlin`). */
export function filterTimeZones(zones: readonly string[], query: string, limit = 80): string[] {
  const normalize = (value: string) => value.toLowerCase().replace(/[_/]/g, ' ')
  const terms = normalize(query).split(/\s+/).filter(Boolean)
  const matches = terms.length
    ? zones.filter((zone) => terms.every((term) => normalize(zone).includes(term)))
    : zones
  return matches.slice(0, limit)
}
