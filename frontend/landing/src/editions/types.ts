import type { PlanOverrides } from '@scalenderplus/plans'
import type en from '@/locales/en.json'

/**
 * Build-time editions of the landing (`VITE_EDITION`, as in the app: docs/product/editions.md §2).
 * Each folder next to this file exports an `edition` and is selected with the `@edition` alias, so
 * only the chosen edition is bundled; the CI leak check (frontend/app/scripts/check-edition-leak.mjs
 * --root landing) fails the standard build if anything of the poly folder ends up in it.
 */
export type EditionId = 'standard' | 'poly'

type DeepPartial<T> = {
  [K in keyof T]?: K extends 'sections'
    ? Record<string, { title: string; text: string }>
    : T[K] extends string
      ? string
      : T[K] extends unknown[]
        ? unknown[]
        : DeepPartial<T[K]>
}

/**
 * Messages that replace base messages (same keys; arrays such as the demo's viewers and entries or
 * the FAQ are replaced as a whole and may have other items; legal `sections` may gain sections,
 * src/lib/messages.ts).
 */
export type LocaleOverlay = DeepPartial<typeof en>

export interface EditionDefinition {
  id: EditionId
  /** A string unique to this edition that stays in its bundle (the leak check's self-test). */
  marker: string
  /**
   * Default brand (`brand.json`): name and OKLCH hue; `VITE_BRAND_NAME`/`VITE_BRAND_HUE` override
   * them per build. An edition may ship `favicon.svg` and `og/<locale>.png` (else src/assets).
   */
  brand: { name: string; hue: number }
  messages: { en: LocaleOverlay; de: LocaleOverlay }
  /** Limits this edition's deployment configures differently (`Plans__*`, plans.md). */
  plans: PlanOverrides
  /**
   * Whether the edition markets the website importer (its pricing rows). A flag rather than a list
   * of limit keys: string literals in an edition folder are leak-check markers.
   */
  importer: boolean
}
