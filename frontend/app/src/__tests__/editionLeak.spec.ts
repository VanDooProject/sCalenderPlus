import { describe, expect, it } from 'vitest'
import {
  collectMarkers,
  extractStrings,
  findLeaks,
  segments,
} from '../../scripts/check-edition-leak.mjs'

const file = (path: string, content: string | Uint8Array) => ({
  path,
  content: typeof content === 'string' ? new TextEncoder().encode(content) : content,
})

describe('edition leak check', () => {
  it('extracts string literals of code, without import specifiers or comments', () => {
    const source = [
      "import de from './locales/de.json'",
      "// don't pick up comments",
      "export const edition = { id: 'poly', marker: 'scalenderplus-edition-poly' }",
      'const x = `Template ${edition.id} tail`',
      "const lazy = () => import('./lazy-module')",
    ].join('\n')
    expect(extractStrings('index.ts', source)).toEqual([
      'poly',
      'scalenderplus-edition-poly',
      'Template ',
      ' tail',
    ])
  })

  it('extracts JSON messages and the visible text of Vue templates', () => {
    expect(extractStrings('en.json', '{"a":{"b":"One"},"c":"Two","n":3}')).toEqual(['One', 'Two'])
    const vue = [
      '<script setup lang="ts">const label = \'Script string\'</script>',
      '<template><p class="text-sm" title="Hover title">Visible text</p></template>',
    ].join('\n')
    expect(extractStrings('Thing.vue', vue).sort()).toEqual(
      ['Hover title', 'Script string', 'Visible text'].sort(),
    )
  })

  it('splits messages at placeholders and plurals and drops short or letterless pieces', () => {
    expect(segments('Leave {group}? | You left {group} constellation.')).toEqual([
      'You left',
      'constellation.',
    ])
    expect(segments('2024-01-01 Hi')).toEqual(['2024-01-01 Hi'])
    expect(segments('123456')).toEqual([])
  })

  it('derives markers from the edition folder, minus everything shared', () => {
    const markers = collectMarkers(
      [
        file('index.ts', "export const e = { name: 'Brand Poly', marker: 'edition-poly-marker' }"),
        file('locales/en.json', '{"nav":{"groups":"Constellations","calendar":"Calendar"}}'),
        file('logo.svg', '<svg/>'),
      ],
      ['Calendar', 'Brand Poly Plus'],
    )
    expect(markers.strings).toEqual(['Constellations', 'edition-poly-marker'])
    expect([...markers.hashes.values()]).toEqual(['logo.svg'])
  })

  it('reports marker strings, forbidden paths and copied files in a build', () => {
    const markers = collectMarkers(
      [
        file('locales/en.json', '{"a":"Constellations"}'),
        file('logo.png', new Uint8Array([1, 2, 3])),
      ],
      [],
    )
    const dist = [
      file('assets/clean.js', 'const nav = `Groups`'),
      file('assets/leaky.js', 'const nav = `Constellations`'),
      file('assets/map.js', '//# sourceURL=src/editions/poly/index.ts'),
      file('assets/logo-abc123.png', new Uint8Array([1, 2, 3])),
    ]
    expect(findLeaks(dist, markers, ['editions/poly/'])).toEqual([
      { file: 'assets/leaky.js', marker: 'Constellations' },
      { file: 'assets/map.js', marker: 'editions/poly/' },
      { file: 'assets/logo-abc123.png', marker: 'file:logo.png' },
    ])
    expect(findLeaks([dist[0]!], markers, ['editions/poly/'])).toEqual([])
  })
})
