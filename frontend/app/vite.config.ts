/// <reference types="vitest/config" />
import { existsSync, readdirSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig, loadEnv, searchForWorkspaceRoot } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import vueI18n from '@intlify/unplugin-vue-i18n/vite'
import { msw } from 'msw/vite'

// Same-origin in every environment: in dev Vite proxies backend paths to the api (port 5080),
// in production the `web` container (Caddy) does.
const apiTarget = process.env.API_PROXY_TARGET ?? 'http://localhost:5080'
// Same list as the `@api` matcher in deploy/caddy/web.Caddyfile.
const proxiedPaths = ['/api', '/ical', '/dav', '/.well-known', '/health', '/openapi']

// Build-time edition (`VITE_EDITION`, default `standard`): one folder of src/editions, aliased as
// `@edition`, so the other editions' code, wording and assets are never bundled
// (scripts/check-edition-leak.mjs checks the standard build in CI).
const editionsDir = fileURLToPath(new URL('./src/editions', import.meta.url))
const editions = readdirSync(editionsDir, { withFileTypes: true })
  .filter((entry) => entry.isDirectory() && existsSync(`${editionsDir}/${entry.name}/index.ts`))
  .map((entry) => entry.name)

function resolveEdition(mode: string): string {
  const edition = (
    process.env.VITE_EDITION ??
    loadEnv(mode, process.cwd(), 'VITE_').VITE_EDITION ??
    ''
  ).trim()
  if (edition === '') return 'standard'
  if (!editions.includes(edition)) {
    throw new Error(
      `Unknown VITE_EDITION "${edition}": expected one of ${editions.join(', ')} (src/editions/<edition>).`,
    )
  }
  return edition
}

// `--mode mock` (`pnpm dev:mock`): no backend; MSW intercepts requests in the browser with the
// shared handlers from @scalenderplus/api-client/mocks. The plugin serves the service worker
// script; it is not part of normal builds.
export default defineConfig(({ mode }) => {
  const edition = resolveEdition(mode)
  return {
    plugins: [
      vue(),
      tailwindcss(),
      // Precompiles the locale JSON: the runtime-only vue-i18n build ships no message compiler
      // (smaller, and no runtime code generation under the strict CSP). Vitest uses the full build
      // and the plain JSON (the i18n tests read the files as written).
      ...(process.env.VITEST
        ? []
        : [
            vueI18n({
              include: [
                fileURLToPath(new URL('./src/locales/**', import.meta.url)),
                fileURLToPath(new URL(`./src/editions/${edition}/locales/**`, import.meta.url)),
              ],
              runtimeOnly: true,
              strictMessage: true,
              escapeHtml: false,
            }),
          ]),
      ...(mode === 'mock' ? [msw({ mode: 'worker-only' })] : []),
    ],
    resolve: {
      alias: {
        '@edition': `${editionsDir}/${edition}/index.ts`,
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      port: 5173,
      strictPort: true,
      proxy: Object.fromEntries(proxiedPaths.map((path) => [path, { target: apiTarget }])),
      // Vitest also reads the Caddyfile (the CSP test checks its style hashes against reka-ui).
      ...(process.env.VITEST
        ? {
            fs: {
              allow: [
                searchForWorkspaceRoot(process.cwd()),
                fileURLToPath(new URL('../../deploy/caddy', import.meta.url)),
              ],
            },
          }
        : {}),
    },
    test: {
      environment: 'jsdom',
      include: ['src/**/*.spec.ts'],
      setupFiles: ['src/__tests__/setup.ts'],
    },
  }
})
