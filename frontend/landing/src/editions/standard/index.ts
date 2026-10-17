import type { EditionDefinition } from '../types'
import brand from './brand.json'

/** The generic, white-labelable edition (default): base messages and plans as they are. */
export const edition: EditionDefinition = {
  id: 'standard',
  marker: 'scalenderplus-landing-edition-standard',
  brand,
  messages: { en: {}, de: {} },
  plans: {},
  importer: true,
}
