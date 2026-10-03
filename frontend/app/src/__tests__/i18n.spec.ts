import { beforeEach, describe, expect, it } from 'vitest'
import en from '@/locales/en.json'
import de from '@/locales/de.json'
import { detectLocale, persistLocale } from '@/i18n'

function keys(messages: object, prefix = ''): string[] {
  return Object.entries(messages).flatMap(([key, value]) =>
    typeof value === 'object' && value !== null
      ? keys(value as object, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  )
}

describe('i18n', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('has the same keys in every locale', () => {
    expect(keys(de).sort()).toEqual(keys(en).sort())
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
