import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  test: {
    environment: 'jsdom',
    include: ['src/**/*.spec.ts'],
    setupFiles: ['src/__tests__/setup.ts'],
    // The contrast test reads tokens.css as text (`?raw`); other CSS stays stubbed.
    css: { include: [/tokens\.css/] },
  },
})
