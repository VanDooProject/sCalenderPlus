/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { msw } from 'msw/vite'

// Same-origin in every environment: in dev Vite proxies backend paths to the api (port 5080),
// in production the `web` container (Caddy) does.
const apiTarget = process.env.API_PROXY_TARGET ?? 'http://localhost:5080'
const proxiedPaths = ['/api', '/ical', '/dav', '/.well-known', '/health']

// `--mode mock` (`pnpm dev:mock`): no backend; MSW intercepts requests in the browser with the
// shared handlers from @scalenderplus/api-client/mocks. The plugin serves the service worker
// script; it is not part of normal builds.
export default defineConfig(({ mode }) => ({
  plugins: [vue(), tailwindcss(), ...(mode === 'mock' ? [msw({ mode: 'worker-only' })] : [])],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: Object.fromEntries(proxiedPaths.map((path) => [path, { target: apiTarget }])),
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.spec.ts'],
  },
}))
