import { createI18n } from 'vue-i18n'
import type { ErrorCode } from '@scalenderplus/api-client'
import { edition } from '@edition'
import type { LocaleOverlay } from './editions/types'
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

/**
 * A node of the message tree, not a message: messages are strings (tests), functions or the ASTs
 * precompiled by the i18n plugin (`{ t: 0, b: … }`, or `{ type: 0, body: … }` unminified), and an
 * overlay message replaces them whole.
 */
function isNamespace(value: unknown): value is Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false
  const node = value as { t?: unknown; type?: unknown }
  return typeof node.t !== 'number' && typeof node.type !== 'number'
}

/** The edition's overlay over the base messages: same keys, the overlay's messages win. */
export function mergeMessages<T extends object>(base: T, overlay: LocaleOverlay): T {
  const result = { ...base } as Record<string, unknown>
  for (const [key, value] of Object.entries(overlay)) {
    if (!(key in result) || value === undefined) continue
    const current = result[key]
    result[key] =
      isNamespace(current) && isNamespace(value)
        ? mergeMessages(current, value as LocaleOverlay)
        : value
  }
  return result as T
}

export function createAppI18n(locale: Locale = detectLocale()) {
  return createI18n<[MessageSchema], Locale>({
    legacy: false,
    locale,
    fallbackLocale: 'en',
    messages: {
      en: mergeMessages(en, edition.messages.en),
      de: mergeMessages(de, edition.messages.de),
    },
  })
}
