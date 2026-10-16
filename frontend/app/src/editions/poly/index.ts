import type { EditionDefinition } from '../types'
import brand from './brand.json'
import de from './locales/de.json'
import en from './locales/en.json'

/**
 * Customer edition for polyamorous and non-monogamous communities: groups are called
 * constellations, everything else is the standard app. The name is a placeholder until the owner
 * decides on the brand.
 */
export const edition: EditionDefinition = {
  id: 'poly',
  marker: 'scalenderplus-edition-poly',
  brand,
  messages: { en, de },
  features: {},
}
