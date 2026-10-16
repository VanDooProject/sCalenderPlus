import type { EditionDefinition } from '../types'

/** The generic, white-labelable edition (default). */
export const edition: EditionDefinition = {
  id: 'standard',
  marker: 'scalenderplus-edition-standard',
  brand: { name: 'sCalenderPlus', hue: 268 },
  messages: { en: {}, de: {} },
  features: {},
}
