// @vitest-environment node
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import { catalogue, withOverrides } from '@scalenderplus/plans'
import { edition as selected } from '@edition'
import type { EditionDefinition } from '@/editions/types'
import { checkBuild } from '../../../app/scripts/check-edition-leak.mjs'

const editions = Object.values(
  import.meta.glob<EditionDefinition>('../editions/*/index.ts', { import: 'edition', eager: true }),
)
const landingDir = resolve(import.meta.dirname, '../..')
const appDir = resolve(import.meta.dirname, '../../../app')
const readJson = (path: string) => JSON.parse(readFileSync(path, 'utf8')) as { words: string[] }

describe('landing editions', () => {
  it('finds the standard and poly editions and selects standard by default', () => {
    expect(editions.map((e) => e.id).sort()).toEqual(['poly', 'standard'])
    expect(selected.id).toBe('standard')
  })

  it.each(editions.map((e) => [e.id, e] as const))('%s has valid defaults', (id, edition) => {
    expect(edition.brand.name.trim()).not.toBe('')
    expect(edition.brand.hue).toBeGreaterThanOrEqual(0)
    expect(edition.brand.hue).toBeLessThanOrEqual(360)
    expect(edition.marker).toContain(id)
    expect(() => withOverrides(catalogue, edition.plans)).not.toThrow()
  })

  it('the poly landing denies at least the words the app denies', () => {
    const app = readJson(join(appDir, 'src/editions/poly/leak-words.json')).words
    const landing = readJson(join(landingDir, 'src/editions/poly/leak-words.json')).words
    for (const word of app) expect(landing).toContain(word)
    expect(landing).toContain(editions.find((e) => e.id === 'poly')!.marker)
  })

  it('the poly landing stays discreet: no explicit or adult wording', () => {
    const poly = editions.find((e) => e.id === 'poly')!
    const text = JSON.stringify(poly.messages)
    const explicit = /(?<![\p{L}])(sex|erot|fetish|kink|bdsm|adult|nsfw|porn|nude|nackt|swinger)/giu
    expect(text.match(explicit)).toBeNull()
  })
})

describe('leak check on the landing layout (check-edition-leak.mjs --root)', () => {
  let dist: string | undefined
  afterEach(() => {
    if (dist) rmSync(dist, { recursive: true, force: true })
  })

  function build(files: Record<string, string>) {
    dist = mkdtempSync(join(tmpdir(), 'landing-dist-'))
    for (const [path, content] of Object.entries(files)) {
      mkdirSync(join(dist, path, '..'), { recursive: true })
      writeFileSync(join(dist, path), content)
    }
    return dist
  }

  const standard = editions.find((e) => e.id === 'standard')!
  const ownMarkers = `${standard.marker} ${standard.brand.name}`

  it('passes a build with only standard strings', () => {
    const distDir = build({ 'index.html': `<p>${ownMarkers}</p>` })
    expect(checkBuild({ appDir: landingDir, distDir, edition: 'standard' })).toEqual([])
  })

  it('finds poly wording and leak words in a standard build', () => {
    const poly = editions.find((e) => e.id === 'poly')!
    const heroTitle = (poly.messages.en.hero as { title: string }).title
    const distDir = build({
      'index.html': `<p>${ownMarkers}</p>`,
      'assets/app.js': `const t = ${JSON.stringify(heroTitle)}; const g = "Constellations"`,
    })
    const problems = checkBuild({ appDir: landingDir, distDir, edition: 'standard' })
    expect(problems.some((p) => p.includes(JSON.stringify(heroTitle)))).toBe(true)
    expect(problems.some((p) => p.includes('"constellation"'))).toBe(true)
  })
})
