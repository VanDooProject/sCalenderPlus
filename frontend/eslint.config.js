import pluginVue from 'eslint-plugin-vue'
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'
import skipFormatting from '@vue/eslint-config-prettier/skip-formatting'
import globals from 'globals'

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
              message: "Import the build's edition from '@edition', not an edition folder.",
            },
          ],
        },
      ],
    },
  },
  skipFormatting,
)
