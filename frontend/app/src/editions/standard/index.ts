import type { EditionDefinition } from '../types'
import brand from './brand.json'

/** The generic, white-labelable edition (default). */
export const edition: EditionDefinition = {
  id: 'standard',
  marker: 'scalenderplus-edition-standard',
  brand,
  messages: { en: {}, de: {} },
  features: {},
}
