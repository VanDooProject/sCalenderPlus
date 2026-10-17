import { describe, expect, it } from 'vitest'
import type { EditionDefinition } from '@/editions/types'
import de from '@/locales/de.json'
import en from '@/locales/en.json'
import { format, mergeMessages, translate, type Messages } from '@/lib/messages'

// Every edition folder (tests only: page code imports the selected one via `@edition`).
const editions = Object.values(
  import.meta.glob<EditionDefinition>('../editions/*/index.ts', { import: 'edition', eager: true }),
)
const base = { en, de } as const

/** Leaf paths with their strings; array items are addressed by index. */
function leaves(value: unknown, prefix = ''): [string, string][] {
  if (typeof value === 'string') return [[prefix, value]]
  if (typeof value === 'object' && value !== null) {
    return Object.entries(value).flatMap(([key, child]) =>
      leaves(child, prefix === '' ? key : `${prefix}.${key}`),
    )
  }
  return []
}

const placeholders = (message: string) =>
  [...message.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort()

describe('base locales', () => {
  it('en and de have the same messages', () => {
    expect(leaves(de).map(([key]) => key)).toEqual(leaves(en).map(([key]) => key))
  })

  it('en and de messages use the same placeholders and plural forms', () => {
    const german = new Map(leaves(de))
    for (const [key, message] of leaves(en)) {
      const other = german.get(key)!
      expect(placeholders(other), key).toEqual(placeholders(message))
      expect(other.split('|').length, key).toBe(message.split('|').length)
    }
  })

  it('the demo has a level for every viewer of every entry', () => {
    for (const messages of [en, de]) {
      const ids = messages.demo.viewers.map((v) => v.id).sort()
      for (const event of messages.demo.events) {
        expect(Object.keys(event.access).sort()).toEqual(ids)
        for (const level of Object.values(event.access)) {
          expect(['edit', 'read', 'free_busy', 'none']).toContain(level)
        }
      }
    }
  })
})

describe.each(editions.map((e) => [e.id, e] as const))('edition %s', (id, edition) => {
  it.each(['en', 'de'] as const)('%s overlay merges over the base messages', (locale) => {
    expect(() =>
      mergeMessages(base[locale] as Messages, edition.messages[locale] as Messages),
    ).not.toThrow()
  })

  it.each(['en', 'de'] as const)(
    '%s overlay keeps the placeholders of replaced messages',
    (locale) => {
      const original = new Map(leaves(base[locale]))
      for (const [key, message] of leaves(edition.messages[locale])) {
        // Arrays (demo, FAQ, features) are replaced as a whole and may differ.
        if (/\.\d+(\.|$)/.test(key) || !original.has(key)) continue
        expect(placeholders(message), `${id} ${locale}: ${key}`).toEqual(
          placeholders(original.get(key)!),
        )
      }
    },
  )

  it('has the same overlay keys in en and de', () => {
    const shape = (messages: object) =>
      leaves(messages)
        .map(([key]) => key)
        .sort()
    expect(shape(edition.messages.de)).toEqual(shape(edition.messages.en))
  })

  it('demo entries give every viewer a known level', () => {
    for (const locale of ['en', 'de'] as const) {
      const merged = mergeMessages(base[locale] as Messages, edition.messages[locale] as Messages)
      const demo = merged.demo as unknown as typeof en.demo
      const ids = demo.viewers.map((v) => v.id).sort()
      for (const event of demo.events) expect(Object.keys(event.access).sort()).toEqual(ids)
    }
  })
})

describe('mergeMessages', () => {
  it('replaces strings and arrays, merges objects', () => {
    const merged = mergeMessages(
      { a: { b: 'x', c: 'y' }, list: ['1', '2'] },
      { a: { b: 'z' }, list: ['3'] },
    )
    expect(merged).toEqual({ a: { b: 'z', c: 'y' }, list: ['3'] })
  })

  it('rejects unknown keys except new legal sections', () => {
    expect(() => mergeMessages({ a: 'x' }, { b: 'y' })).toThrow(/"b" is not in the base/)
    const merged = mergeMessages(
      { sections: { one: { title: 'T', text: 'x' } } },
      { sections: { two: { title: 'U', text: 'y' } } },
    )
    expect(Object.keys(merged.sections as object)).toEqual(['one', 'two'])
  })
})

describe('format', () => {
  it('fills placeholders and picks plural forms', () => {
    expect(format('Hi {name}', { name: 'Vic' })).toBe('Hi Vic')
    expect(format('{count} entry | {count} entries', { count: 1 })).toBe('1 entry')
    expect(format('{count} entry | {count} entries', { count: 3 })).toBe('3 entries')
    expect(format('Keep {unknown}')).toBe('Keep {unknown}')
  })

  it('throws on missing or non-string messages', () => {
    expect(() => translate({ a: { b: 'x' } }, 'a.c')).toThrow(/Missing message "a.c"/)
    expect(() => translate({ a: { b: 'x' } }, 'a')).toThrow(/not a string/)
  })
})
