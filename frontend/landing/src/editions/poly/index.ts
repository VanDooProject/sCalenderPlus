import type { EditionDefinition } from '../types'
import brand from './brand.json'
import de from './locales/de.json'
import en from './locales/en.json'

/**
 * Customer edition for partners, metamours and constellations (docs/product/editions.md §6): the
 * same calendar engine, marketed discreetly as a calendar for relationships – no explicit content,
 * no add-ons. Groups are called constellations. The more generous Free limits are the proposal of
 * docs/product/plans.md §Per-edition defaults; the poly deployment sets the same `Plans__Free__*`.
 */
export const edition: EditionDefinition = {
  id: 'poly',
  marker: 'scalenderplus-landing-edition-poly',
  brand,
  messages: { en, de },
  plans: { free: { eventsWithOverrides: 50, overridesPerEvent: 10 } },
  // The importer is not part of this edition's marketing (pricing rows: src/lib/pricing.ts).
  importer: false,
}
