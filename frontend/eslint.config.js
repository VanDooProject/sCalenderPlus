import pluginVue from 'eslint-plugin-vue'
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'
import skipFormatting from '@vue/eslint-config-prettier/skip-formatting'
import globals from 'globals'

const editionMessage = "Import the build's edition from '@edition', not an edition folder."

// A module source naming an edition folder (`../editions/poly/…`, `@/editions/standard`; not
// `editions/types`) or an `@edition/…` subpath. esquery regexes can't contain a slash: \u002f.
const editionSource = String.raw`/(^|\u002f|\\)editions(\u002f|\\)(?!types(\.ts)?$)|^@edition(\u002f|\\)/`
const editionSourceSelectors = [
  // import('../editions/poly/x'), import(`../editions/${id}/index.ts`)
  `ImportExpression > Literal[value=${editionSource}]`,
  `ImportExpression > TemplateLiteral > TemplateElement[value.raw=${editionSource}]`,
  // import.meta.glob('../editions/*/index.ts'), also with an array of patterns
  `CallExpression[callee.object.type='MetaProperty'][callee.property.name=/^glob/] Literal[value=${editionSource}]`,
  `CallExpression[callee.object.type='MetaProperty'][callee.property.name=/^glob/] TemplateElement[value.raw=${editionSource}]`,
  // new URL('../editions/poly/logo.svg', import.meta.url)
  `NewExpression[callee.name='URL'] > Literal[value=${editionSource}]`,
  `NewExpression[callee.name='URL'] > TemplateLiteral > TemplateElement[value.raw=${editionSource}]`,
]

export default defineConfigWithVueTs(
  {
    name: 'scal/files',
    files: ['**/*.{ts,mts,tsx,vue}'],
  },
  {
    name: 'scal/ignores',
    ignores: ['**/dist/**', '**/coverage/**', '**/node_modules/**'],
  },
  pluginVue.configs['flat/recommended'],
  vueTsConfigs.recommended,
  {
    name: 'scal/rules',
    languageOptions: {
      globals: { ...globals.browser },
    },
    rules: {
      'vue/multi-word-component-names': 'off',
      'vue/require-default-prop': 'off',
    },
  },
  {
    name: 'scal/node-files',
    files: ['**/*.config.{js,ts,mjs}', '**/scripts/**/*.{js,mjs}'],
    languageOptions: {
      globals: { ...globals.node },
    },
  },
  {
    name: 'scal/tests',
    files: ['**/*.spec.ts'],
    rules: {
      // Specs define small wrapper components inline.
      'vue/one-component-per-file': 'off',
    },
  },
  {
    // Editions are selected at build time through the `@edition` alias; importing a folder directly
    // would bundle it into every edition (app/src/editions/types.ts). Tests may look at all of them.
    // Static imports, dynamic `import()`, `import.meta.glob()` and `new URL(…, import.meta.url)` all
    // make Vite bundle the target, so all four are checked; `@edition/…` subpaths (`@edition/../poly`)
    // are rejected too, the alias names exactly one module.
    name: 'scal/editions',
    files: ['app/src/**/*.{ts,vue}'],
    ignores: ['app/src/__tests__/**'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/editions/*', '!**/editions/types'],
              message: editionMessage,
            },
            { regex: '^@edition/', message: editionMessage },
          ],
        },
      ],
      'no-restricted-syntax': [
        'error',
        ...editionSourceSelectors.map((selector) => ({ selector, message: editionMessage })),
      ],
    },
  },
  skipFormatting,
)
