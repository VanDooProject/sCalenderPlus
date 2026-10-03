/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

// Same-origin in every environment: in dev Vite proxies backend paths to the api (port 5080),
// in production the `web` container (Caddy) does.
const apiTarget = process.env.API_PROXY_TARGET ?? 'http://localhost:5080'
const proxiedPaths = ['/api', '/ical', '/dav', '/.well-known', '/health']

export default defineConfig({
  plugins: [vue(), tailwindcss()],
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
})
