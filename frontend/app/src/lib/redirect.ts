import type { LocationQueryValue } from 'vue-router'

/**
 * The `?next=` target after signing in, only when it is an in-app path (no scheme, no `//host`,
 * no backslash tricks), else undefined.
 */
export function safeNext(
  value: LocationQueryValue | LocationQueryValue[] | undefined,
): string | undefined {
  const next = Array.isArray(value) ? value[0] : value
  if (typeof next !== 'string' || !next.startsWith('/')) return undefined
  if (next.startsWith('//') || next.includes('\\')) return undefined
  if ([...next].some((c) => c.charCodeAt(0) < 0x20)) return undefined
  return next
}
