import type en from '@/locales/en.json'

/**
 * Build-time editions (`VITE_EDITION`, see vite.config.ts). Each folder next to this file exports an
 * `edition` and is selected with the `@edition` alias, so only the chosen edition is bundled: never
 * import an edition folder directly from app code (the CI leak check fails the standard build if any
 * string of the poly folder ends up in it, see scripts/check-edition-leak.mjs).
 */
export type EditionId = 'standard' | 'poly'

type DeepPartial<T> = { [K in keyof T]?: T[K] extends string ? string : DeepPartial<T[K]> }

/** Messages that replace base locale messages (same keys and placeholders, only the changed ones). */
export type LocaleOverlay = DeepPartial<typeof en>

export interface EditionDefinition {
  id: EditionId
  /** A string unique to this edition that stays in its bundle (the leak check looks for it). */
  marker: string
  /** Defaults of the runtime branding (`brand` in /config.json overrides them per deployment). */
  brand: {
    name: string
    /** OKLCH hue (0–360) of the brand palette. */
    hue: number
  }
  messages: { en: LocaleOverlay; de: LocaleOverlay }
  /** Switches for edition-only modules (none yet). */
  features: Readonly<Record<string, boolean>>
}
