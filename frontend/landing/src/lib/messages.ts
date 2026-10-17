/**
 * Tiny message lookup for the static pages (no vue-i18n: every page is prerendered in one locale,
 * nothing switches at runtime). Messages are the base locale JSON with the edition's overlay merged
 * over it; `{name}` placeholders are filled from params, and `a | b` picks the singular or plural by
 * `count`. A missing key throws, so the prerender fails instead of shipping a raw key.
 */
export type Messages = { [key: string]: Message }
export type Message = string | Messages | Message[]

const isObject = (value: unknown): value is Messages =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

/**
 * `base` with `overlay` merged over it: objects recursively, strings and arrays replaced. An overlay
 * may only add keys inside a `sections` object (an edition's extra legal section, appended after the
 * base sections); any other unknown key is a typo and throws.
 */
export function mergeMessages(base: Messages, overlay: Messages, path = ''): Messages {
  const result: Messages = { ...base }
  for (const [key, value] of Object.entries(overlay)) {
    if (!(key in base) && !path.endsWith('sections.')) {
      throw new Error(`Overlay key "${path}${key}" is not in the base messages`)
    }
    const current = base[key]
    result[key] =
      isObject(current) && isObject(value) ? mergeMessages(current, value, `${path}${key}.`) : value
  }
  return result
}

export type Params = Record<string, string | number>

export function format(message: string, params: Params = {}): string {
  let text = message
  if (text.includes('|') && typeof params.count === 'number') {
    const forms = text.split('|').map((form) => form.trim())
    text = (params.count === 1 ? forms[0] : forms[forms.length - 1]) ?? text
  }
  return text.replace(/\{(\w+)\}/g, (match, name: string) =>
    name in params ? String(params[name]) : match,
  )
}

export function lookup(messages: Messages, key: string): Message {
  let value: Message | undefined = messages
  for (const part of key.split('.')) {
    value = isObject(value) ? value[part] : Array.isArray(value) ? value[Number(part)] : undefined
    if (value === undefined) throw new Error(`Missing message "${key}"`)
  }
  return value
}

export function translate(messages: Messages, key: string, params?: Params): string {
  const value = lookup(messages, key)
  if (typeof value !== 'string') throw new Error(`Message "${key}" is not a string`)
  return format(value, params)
}
