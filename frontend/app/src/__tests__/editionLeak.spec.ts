import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import {
  checkBuild,
  collectMarkers,
  dataUriPayloads,
  extractStrings,
  findLeaks,
  findWords,
  leakWords,
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

  describe('assets', () => {
    const svg = [
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">',
      '  <title>Poly mark</title>',
      '  <rect width="32" height="32" rx="8" fill="#a624a0"/>',
      '</svg>',
    ].join('\n')
    const png = new Uint8Array(Array.from({ length: 64 }, (_, i) => (i * 37) % 256))
    const markers = collectMarkers([file('mark.svg', svg), file('photo.png', png)], [])
    const base64 = (bytes: Uint8Array) => Buffer.from(bytes).toString('base64')

    // Vite's inlined SVG: whitespace collapsed, `'` quotes, `<`, `>` and `#` percent-encoded.
    const viteSvgUri =
      'data:image/svg+xml,' +
      svg
        .replace(/>\s+</g, '><')
        .replaceAll('"', "'")
        .replaceAll('#', '%23')
        .replaceAll('<', '%3c')
        .replaceAll('>', '%3e')
        .replace(/\s+/g, '%20')

    it('takes the text of SVGs as markers, not their shapes', () => {
      expect(extractStrings('mark.svg', svg)).toEqual(['Poly mark'])
      expect(markers.strings).toEqual(['Poly mark'])
    })

    it.each([
      ['a copied file', file('assets/photo-x1.png', png), 'file:photo.png'],
      [
        'a base64 data: URI',
        file('assets/index.css', `.a{background:url(data:image/png;base64,${base64(png)})}`),
        'file:photo.png',
      ],
      ['base64 in code', file('assets/x.js', `const p = "${base64(png)}"`), 'file:photo.png'],
      [
        'a URL-encoded SVG data: URI',
        file('assets/x.js', `const u = "${viteSvgUri}"`),
        'file:mark.svg',
      ],
      [
        'a base64 SVG data: URI',
        file('assets/x.css', `.m{mask:url("data:image/svg+xml;base64,${btoa(svg)}")}`),
        'file:mark.svg',
      ],
      [
        'raw SVG markup in code',
        file('assets/x.js', `const s = ${JSON.stringify(svg)}`),
        'file:mark.svg',
      ],
    ])('finds an asset inlined as %s', (_, built, marker) => {
      expect(findLeaks([built], markers).map((leak) => leak.marker)).toContain(marker)
    })

    it('searches the text of data: URIs for string markers', () => {
      const leaks = findLeaks([file('assets/x.js', '"data:text/plain,Poly%20mark"')], markers)
      expect(leaks).toEqual([{ file: 'assets/x.js', marker: 'Poly mark' }])
    })

    it('decodes base64 and percent-encoded data: URIs', () => {
      const payloads = dataUriPayloads('a "data:,Hi%20there" b url(data:text/plain;base64,SGk=)')
      expect(payloads.map((p) => new TextDecoder().decode(p))).toEqual(['Hi there', 'Hi'])
    })

    it('ignores other images', () => {
      const other = new Uint8Array(png.map((b) => b ^ 1))
      const built = [
        file('assets/other.png', other),
        file('assets/x.css', `url(data:image/png;base64,${base64(other)})`),
        file('favicon.svg', svg.replace('#a624a0', '#4f46e5').replace('Poly mark', 'Calendar')),
      ]
      expect(findLeaks(built, markers)).toEqual([])
    })
  })

  describe('stylesheets', () => {
    const css = [
      '/* "comment strings" are not markers */',
      ':root { --poly-badge: "Constellation member"; }',
      ".badge::after { content: 'Metamour \\'s note'; }",
    ].join('\n')

    it('extracts quoted strings, without comments', () => {
      expect(extractStrings('theme.css', css)).toEqual(['Constellation member', "Metamour 's note"])
    })

    it('finds a CSS string in a bundled stylesheet', () => {
      const markers = collectMarkers([file('theme.css', css)], [])
      const built = file('assets/index-abc.css', '.badge:after{content:"Constellation member"}')
      expect(findLeaks([built], markers)).toEqual([
        { file: 'assets/index-abc.css', marker: 'Constellation member' },
      ])
    })
  })

  it('reports file types it cannot read', () => {
    const markers = collectMarkers(
      [file('notes.md', '# Poly'), file('index.ts', "'Hello poly'")],
      [],
    )
    expect(markers.unsupported).toEqual(['notes.md'])
  })

  describe('leak words', () => {
    const words = ['constellation', 'polyamor', 'kink', 'scalenderplus-edition-poly']

    it('matches case-insensitively at the start of a word', () => {
      const hits = findWords(
        [
          file('index.html', '<title>Polyamorous Planner</title>'),
          file('assets/a.js', 'const t = "My CONSTELLATIONS"; const k = "kinky"'),
          file('assets/b.js', 'const m = "scalenderplus-edition-poly"'),
        ],
        words,
      )
      expect(hits.map((hit) => [hit.file, hit.word])).toEqual([
        ['index.html', 'polyamor'],
        ['assets/a.js', 'constellation'],
        ['assets/a.js', 'kink'],
        ['assets/b.js', 'scalenderplus-edition-poly'],
      ])
    })

    it('does not trip over polyfills, polylines or words inside other words', () => {
      const built = file(
        'assets/vendor.js',
        'import "temporal-polyfill"; drawPolyline(); const skinks = 1; const x_kink = 2',
      )
      expect(findWords([built], words)).toEqual([])
    })

    it('searches text files and data: URIs, not binaries', () => {
      expect(findWords([file('font.woff2', 'kink')], words)).toEqual([])
      const uri = file('assets/x.css', 'url("data:image/svg+xml,%3ctext%3eKink%3c/text%3e")')
      expect(findWords([uri], words).map((hit) => hit.word)).toEqual(['kink'])
    })

    it('reads the list from the edition folder and never uses it as markers', () => {
      const list = file('leak-words.json', '{"description":"Not shipped","words":["Polycule"]}')
      expect(leakWords([list])).toEqual(['Polycule'])
      expect(leakWords([])).toEqual([])
      expect(collectMarkers([list], []).strings).toEqual([])
      expect(() => leakWords([file('leak-words.json', '{"words":[""]}')])).toThrow(/words/)
    })
  })

  describe('checkBuild', () => {
    let root = ''
    afterEach(() => rmSync(root, { recursive: true, force: true }))

    function tree(files: Record<string, string>) {
      root = mkdtempSync(join(tmpdir(), 'edition-leak-'))
      for (const [path, content] of Object.entries(files)) {
        mkdirSync(dirname(join(root, path)), { recursive: true })
        writeFileSync(join(root, path), content)
      }
      return { appDir: root, distDir: join(root, 'dist') }
    }

    const sources = {
      'src/locales/en.json': '{"nav":{"groups":"Groups"}}',
      'src/editions/standard/index.ts': "export const edition = { marker: 'edition-standard-x' }",
      'src/editions/poly/index.ts': "export const edition = { marker: 'edition-poly-x' }",
      'src/editions/poly/leak-words.json': '{"words":["metamour","edition-poly"]}',
    }

    it('passes a clean build', () => {
      const dirs = tree({ ...sources, 'dist/assets/app.js': '"edition-standard-x";"Groups"' })
      expect(checkBuild({ ...dirs, edition: 'standard' })).toEqual([])
    })

    it('fails on leak words and unsupported files of the other edition', () => {
      const dirs = tree({
        ...sources,
        'src/editions/poly/readme.txt': 'notes',
        'dist/assets/app.js': '"edition-standard-x";"Your Metamours"',
      })
      const problems = checkBuild({ ...dirs, edition: 'standard' })
      expect(problems).toHaveLength(2)
      expect(problems[0]).toMatch(/poly\/readme\.txt: file type not supported/)
      expect(problems[1]).toMatch(/assets\/app\.js: contains "metamour" .*Your Metamours/)
    })

    it("self-tests the built edition's own leak words", () => {
      const dirs = tree({
        ...sources,
        'src/editions/poly/leak-words.json': '{"words":["metamour"]}',
        'dist/assets/app.js': '"edition-poly-x"',
      })
      expect(checkBuild({ ...dirs, edition: 'poly' })).toEqual([
        expect.stringMatching(/Self-test: none of the poly edition's own leak words/),
      ])
    })
  })
})
