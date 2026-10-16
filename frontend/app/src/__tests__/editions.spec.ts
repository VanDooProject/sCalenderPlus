import { describe, expect, it } from 'vitest'
import { edition as selected } from '@edition'
import type { EditionDefinition } from '@/editions/types'
import { mergeMessages } from '@/i18n'
import en from '@/locales/en.json'
import de from '@/locales/de.json'

// Every edition folder (tests only: app code imports the selected one via `@edition`).
const editions = Object.values(
  import.meta.glob<EditionDefinition>('../editions/*/index.ts', {
    import: 'edition',
    eager: true,
  }),
)
const base = { en, de } as const

type Tree = { [key: string]: string | Tree }

function entries(messages: object, prefix = ''): [string, string][] {
  return Object.entries(messages).flatMap(([key, value]) =>
    typeof value === 'object' && value !== null
      ? entries(value as object, `${prefix}${key}.`)
      : [[`${prefix}${key}`, value as string]],
  )
}

function lookup(messages: object, path: string): unknown {
  return path
    .split('.')
    .reduce<unknown>((node, key) => (node as Record<string, unknown> | undefined)?.[key], messages)
}

const placeholders = (message: string) =>
  [...message.matchAll(/\{[^}]*\}/g)].map((m) => m[0]).sort()
const withoutPlaceholders = (message: string) => message.replace(/\{[^}]*\}/g, '')

describe('editions', () => {
  it('finds the standard and poly editions', () => {
    expect(editions.map((e) => e.id).sort()).toEqual(['poly', 'standard'])
  })

  it('selects the standard edition by default', () => {
    expect(selected.id).toBe('standard')
  })

  describe.each(editions.map((e) => [e.id, e] as const))('%s', (id, edition) => {
    it('has valid brand defaults and a unique marker', () => {
      expect(edition.brand.name.trim()).not.toBe('')
      expect(edition.brand.hue).toBeGreaterThanOrEqual(0)
      expect(edition.brand.hue).toBeLessThanOrEqual(360)
      expect(edition.marker).toContain(id)
      expect(editions.filter((e) => e.marker === edition.marker)).toHaveLength(1)
    })

    it.each(['en', 'de'] as const)(
      'overlays only existing %s keys, with the same placeholders',
      (locale) => {
        for (const [key, message] of entries(edition.messages[locale])) {
          const original = lookup(base[locale], key)
          expect(typeof original, `${id} ${locale}: ${key} is not a base message`).toBe('string')
          expect(message.trim(), `${id} ${locale}: ${key} is empty`).not.toBe('')
          expect(placeholders(message), `${id} ${locale}: ${key}`).toEqual(
            placeholders(original as string),
          )
        }
      },
    )
  })

  it('calls groups constellations everywhere in the poly edition', () => {
    const poly = editions.find((e) => e.id === 'poly')!
    const leftovers = (messages: object, pattern: RegExp) =>
      entries(messages).filter(([, message]) => pattern.test(withoutPlaceholders(message)))

    expect(leftovers(mergeMessages(en, poly.messages.en), /\bgroups?\b/i)).toEqual([])
    expect(leftovers(mergeMessages(de, poly.messages.de), /gruppe/i)).toEqual([])
    expect(lookup(mergeMessages(en, poly.messages.en), 'nav.groups')).toBe('Constellations')
    expect(lookup(mergeMessages(de, poly.messages.de), 'nav.groups')).toBe('Konstellationen')
  })
})

describe('mergeMessages', () => {
  const messages: Tree = { a: { b: 'B', c: 'C' }, d: 'D' }

  it('replaces overlaid messages and keeps the rest', () => {
    expect(mergeMessages(messages, { a: { b: 'B2' } } as never)).toEqual({
      a: { b: 'B2', c: 'C' },
      d: 'D',
    })
    expect(messages).toEqual({ a: { b: 'B', c: 'C' }, d: 'D' })
  })

  it('ignores keys the base does not have', () => {
    expect(mergeMessages(messages, { x: 'X', a: { y: 'Y' } } as never)).toEqual(messages)
  })

  it.each([
    [
      'minified',
      { greet: { t: 0, b: { t: 2, i: [{ t: 3 }], s: 'Hello' } } },
      {
        greet: {
          t: 0,
          b: {
            t: 2,
            i: [
              { t: 3, v: 'Hi ' },
              { t: 4, k: 'name' },
            ],
          },
        },
      },
    ],
    [
      'unminified',
      { greet: { type: 0, body: { static: 'Hello', items: [] } } },
      { greet: { type: 0, body: { items: [{ type: 3, value: 'Hi' }] } } },
    ],
  ])(
    'replaces precompiled (%s) messages whole instead of merging their AST',
    (_, compiled, overlay) => {
      expect(mergeMessages(compiled, overlay as never)).toEqual(overlay)
    },
  )
})
