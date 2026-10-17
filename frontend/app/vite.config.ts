/// <reference types="vitest/config" />
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig, loadEnv, searchForWorkspaceRoot, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import vueI18n from '@intlify/unplugin-vue-i18n/vite'
import { msw } from 'msw/vite'
import { VitePWA } from 'vite-plugin-pwa'
import {
  brandThemeColor,
  iconFiles,
  networkOnlyNavigations,
  webManifest,
} from './src/lib/webManifest'

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

const escapeHtml = (text: string) => text.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`)

/**
 * The edition's static brand before /config.json loads: its name as index.html's `<title>`, its
 * `favicon.svg` and PNG icons (`icons/`; else the defaults in src/assets) served and emitted at the
 * root, and the web app manifest (`/manifest.webmanifest`: name, theme colour of its hue, icons), so
 * another edition's name, favicon or icons never end up in the build.
 */
function editionShell(edition: string): Plugin {
  const dir = `${editionsDir}/${edition}`
  const brand = JSON.parse(readFileSync(`${dir}/brand.json`, 'utf8')) as {
    name: string
    hue: number
  }
  const assets = fileURLToPath(new URL('./src/assets', import.meta.url))
  const own = (path: string) =>
    existsSync(`${dir}/${path}`) ? `${dir}/${path}` : `${assets}/${path}`
  const files: Record<string, { path: string; type: string }> = {
    'favicon.svg': { path: own('favicon.svg'), type: 'image/svg+xml' },
    ...Object.fromEntries(
      iconFiles.map((name) => [name, { path: own(`icons/${name}`), type: 'image/png' }]),
    ),
  }
  const manifest = `${JSON.stringify(webManifest(brand), null, 2)}\n`
  const headTags = [
    '<link rel="manifest" href="/manifest.webmanifest" />',
    '<link rel="apple-touch-icon" href="/apple-touch-icon.png" />',
    `<meta name="theme-color" content="${brandThemeColor(brand.hue)}" />`,
  ]
  return {
    name: 'scal-edition-shell',
    transformIndexHtml: (html) =>
      html
        .replace(/<title>[^<]*<\/title>/, `<title>${escapeHtml(brand.name)}</title>`)
        .replace('</head>', `  ${headTags.join('\n    ')}\n  </head>`),
    configureServer(server) {
      for (const [name, file] of Object.entries(files)) {
        server.middlewares.use(`/${name}`, (_req, res) => {
          res.setHeader('Content-Type', file.type)
          res.setHeader('Cache-Control', 'no-cache')
          res.end(readFileSync(file.path))
        })
      }
      server.middlewares.use('/manifest.webmanifest', (_req, res) => {
        res.setHeader('Content-Type', 'application/manifest+json')
        res.setHeader('Cache-Control', 'no-cache')
        res.end(manifest)
      })
    },
    generateBundle() {
      for (const [name, file] of Object.entries(files)) {
        this.emitFile({ type: 'asset', fileName: name, source: readFileSync(file.path) })
      }
      this.emitFile({ type: 'asset', fileName: 'manifest.webmanifest', source: manifest })
    },
  }
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
      editionShell(edition),
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
      // Installable app with an offline shell: the service worker precaches the built app (HTML,
      // scripts, styles, fonts, icons) and answers navigations with index.html. Nothing else is
      // routed through it, so api responses, iCal feeds, /config.json and the manifest always come
      // from the network and are never cached. Only production builds register it (src/pwa.ts; in
      // dev and mock mode MSW owns the scope); a new version waits for the user (UpdatePrompt.vue).
      VitePWA({
        registerType: 'prompt',
        injectRegister: false,
        manifest: false,
        devOptions: { enabled: false },
        workbox: {
          globPatterns: ['**/*.{html,js,css,woff2,svg,png}'],
          navigateFallback: '/index.html',
          navigateFallbackDenylist: networkOnlyNavigations(proxiedPaths),
          runtimeCaching: [],
          cleanupOutdatedCaches: true,
          // Hashed file names: no cache-busting query needed.
          dontCacheBustURLsMatching: /^assets\//,
        },
      }),
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
