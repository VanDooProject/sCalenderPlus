import { resolve } from 'node:path'
import { ESLint } from 'eslint'
import { describe, expect, it } from 'vitest'

// The `scal/editions` lint rule (frontend/eslint.config.js): app code reaches an edition only through
// the `@edition` alias, whatever form the import takes.
const eslint = new ESLint({ cwd: resolve(import.meta.dirname, '../../..') })

async function editionErrors(code: string, filePath = 'app/src/probe.ts') {
  const [result] = await eslint.lintText(code, { filePath })
  return result!.messages
    .filter((m) => m.ruleId === 'no-restricted-imports' || m.ruleId === 'no-restricted-syntax')
    .map((m) => m.line)
}

describe('scal/editions lint rule', () => {
  it.each([
    ["import { edition } from '@/editions/poly'", 'static import'],
    ["import de from '@edition/../poly/locales/de.json'", '@edition subpath'],
    ["export const m = () => import('../editions/poly/index.ts')", 'dynamic import'],
    ['export const m = (id: string) => import(`@/editions/${id}/index.ts`)', 'template import()'],
    ["export const m = () => import('@edition/../poly/index.ts')", 'dynamic @edition subpath'],
    ["export const m = import.meta.glob('../editions/*/index.ts', { eager: true })", 'glob'],
    ["export const m = import.meta.glob(['./views/*.vue', '/src/editions/poly/**'])", 'glob array'],
    ["export const m = new URL('../editions/poly/favicon.svg', import.meta.url)", 'new URL'],
  ])('rejects %s (%s)', async (code) => {
    expect(await editionErrors(code)).toEqual([1])
  })

  it('allows the alias, the shared types, unrelated imports and plain strings', async () => {
    const code = [
      "import { edition } from '@edition'",
      "import type { EditionDefinition } from '@/editions/types'",
      "export const lazy = () => import('./views/Lazy.vue')",
      "export const views = import.meta.glob('./views/*.vue')",
      "export const text = 'editions/poly/ is only text here'",
      'export const all: EditionDefinition[] = [edition]',
    ].join('\n')
    expect(await editionErrors(code)).toEqual([])
  })

  it('applies to the landing too', async () => {
    const code = "import { edition } from '@/editions/poly'"
    expect(await editionErrors(code, 'landing/src/probe.ts')).toEqual([1])
    expect(await editionErrors(code, 'landing/src/__tests__/probe.spec.ts')).toEqual([])
  })

  it('does not apply to tests', async () => {
    const code = "export const m = import.meta.glob('../editions/*/index.ts', { eager: true })"
    expect(await editionErrors(code, 'app/src/__tests__/probe.spec.ts')).toEqual([])
  })
})
