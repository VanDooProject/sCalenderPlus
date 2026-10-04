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
    files: ['**/*.config.{js,ts,mjs}'],
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
  skipFormatting,
)
