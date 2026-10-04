import { createI18n } from 'vue-i18n'
import type { ErrorCode } from '@scalenderplus/api-client'
import en from './locales/en.json'
import de from './locales/de.json'

export type MessageSchema = typeof en

type Exact<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false
type Assert<T extends true> = T
/**
 * Fails `typecheck` when `errors.code` of the English messages misses an `ErrorCode` of the API
 * contract or keeps a removed one; the key completeness test extends this to every locale.
 */
export type ErrorMessagesCoverContract = Assert<
  Exact<keyof MessageSchema['errors']['code'], ErrorCode>
>
export const supportedLocales = ['en', 'de'] as const
export type Locale = (typeof supportedLocales)[number]

const storageKey = 'scal.locale'

function isLocale(value: unknown): value is Locale {
  return typeof value === 'string' && (supportedLocales as readonly string[]).includes(value)
}

/** Stored choice first, then the browser language, then English. */
export function detectLocale(
  languages: readonly string[] = typeof navigator === 'undefined' ? [] : navigator.languages,
): Locale {
  try {
    const stored = localStorage.getItem(storageKey)
    if (isLocale(stored)) {
      return stored
    }
  } catch {
    // Storage may be unavailable (private mode); fall through.
  }
  for (const language of languages) {
    const base = language.toLowerCase().split('-')[0]
    if (isLocale(base)) {
      return base
    }
  }
  return 'en'
}

export function persistLocale(locale: Locale): void {
  try {
    localStorage.setItem(storageKey, locale)
  } catch {
    // Non-essential convenience; ignore.
  }
}

export function createAppI18n(locale: Locale = detectLocale()) {
  return createI18n<[MessageSchema], Locale>({
    legacy: false,
    locale,
    fallbackLocale: 'en',
    messages: { en, de },
  })
}
