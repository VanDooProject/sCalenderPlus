import { beforeEach, describe, expect, it } from 'vitest'
import { errorCodes } from '@scalenderplus/api-client'
import { detectLocale, persistLocale } from '@/i18n'

// The raw files: imported normally, the i18n plugin precompiles messages to functions/ASTs.
const raw = import.meta.glob<string>('../locales/*.json', {
  query: '?raw',
  import: 'default',
  eager: true,
})
type Messages = { errors: { code: Record<string, string> } } & Record<string, unknown>
const en = JSON.parse(raw['../locales/en.json']!) as Messages
const de = JSON.parse(raw['../locales/de.json']!) as Messages

function keys(messages: object, prefix = ''): string[] {
  return Object.entries(messages).flatMap(([key, value]) =>
    typeof value === 'object' && value !== null
      ? keys(value as object, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  )
}

function values(messages: object): string[] {
  return Object.values(messages).flatMap((value) =>
    typeof value === 'object' && value !== null ? values(value as object) : [String(value)],
  )
}

describe('i18n', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('loads every locale file', () => {
    expect(Object.keys(raw).sort()).toEqual(['../locales/de.json', '../locales/en.json'])
  })

  it('has the same keys in every locale', () => {
    expect(keys(de).sort()).toEqual(keys(en).sort())
  })

  it('has no empty messages', () => {
    for (const messages of [en, de]) {
      expect(values(messages).filter((v) => v.trim() === '')).toEqual([])
    }
  })

  it.each([
    ['en', en],
    ['de', de],
  ] as const)('translates every error code of the API contract (%s)', (_, messages) => {
    expect(Object.keys(messages.errors.code).sort()).toEqual([...errorCodes].sort())
  })

  it('detects the browser language and falls back to English', () => {
    expect(detectLocale(['de-AT', 'en'])).toBe('de')
    expect(detectLocale(['fr-FR'])).toBe('en')
    expect(detectLocale([])).toBe('en')
  })

  it('prefers the persisted choice', () => {
    persistLocale('de')
    expect(detectLocale(['en-US'])).toBe('de')
  })
})
