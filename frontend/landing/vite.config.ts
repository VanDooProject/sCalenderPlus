/// <reference types="vitest/config" />
import { existsSync, readdirSync, readFileSync, rmSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig, loadEnv, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { allPaths, locales } from './src/lib/routes'
import { robots, sitemap } from './src/lib/seo'
import { resolveSiteConfig, siteEnvKeys, type SiteConfig, type SiteEnv } from './src/lib/siteConfig'

// Build-time edition (`VITE_EDITION`, default `standard`), as in the app (frontend/app/vite.config.ts):
// one folder of src/editions, aliased as `@edition`, so the other edition is never bundled
// (`pnpm --filter landing check:edition-leak` checks the build in CI and in landing.Dockerfile).
const editionsDir = fileURLToPath(new URL('./src/editions', import.meta.url))
const editions = readdirSync(editionsDir, { withFileTypes: true })
  .filter((entry) => entry.isDirectory() && existsSync(`${editionsDir}/${entry.name}/index.ts`))
  .map((entry) => entry.name)

function buildEnv(mode: string): Record<string, string | undefined> {
  return { ...loadEnv(mode, process.cwd(), 'VITE_'), ...process.env }
}

function resolveEdition(env: Record<string, string | undefined>): string {
  const edition = (env.VITE_EDITION ?? '').trim()
  if (edition === '') return 'standard'
  if (!editions.includes(edition)) {
    throw new Error(
      `Unknown VITE_EDITION "${edition}": expected one of ${editions.join(', ')} (src/editions/<edition>).`,
    )
  }
  return edition
}

const assetsDir = fileURLToPath(new URL('./src/assets', import.meta.url))

/** Files the edition may replace (`src/editions/<edition>/<path>`, else `src/assets/<path>`). */
const editionFiles: Record<string, string> = {
  'favicon.svg': 'image/svg+xml',
  ...Object.fromEntries(locales.map((locale) => [`og/${locale}.png`, 'image/png'])),
}

/**
 * The edition's static brand: `virtual:brand.css` sets the palette hue (a stylesheet, not an inline
 * style: the CSP allows neither inline styles nor scripts), the favicon and Open Graph images are
 * emitted from the edition folder, and robots.txt and sitemap.xml are generated for the site URL.
 */
function landingShell(edition: string, site: SiteConfig): Plugin {
  const own = (path: string) =>
    existsSync(`${editionsDir}/${edition}/${path}`)
      ? `${editionsDir}/${edition}/${path}`
      : `${assetsDir}/${path}`
  const generated: Record<string, { type: string; body: () => string | Buffer }> = {
    ...Object.fromEntries(
      Object.entries(editionFiles).map(([name, type]) => [
        name,
        { type, body: () => readFileSync(own(name)) },
      ]),
    ),
    'robots.txt': { type: 'text/plain', body: () => robots(site.siteUrl) },
    'sitemap.xml': { type: 'application/xml', body: () => sitemap(site.siteUrl) },
  }
  const brandCss = `:root {\n  --scal-brand-hue: ${site.brandHue};\n}\n`
  return {
    name: 'scal-landing-shell',
    resolveId: (id) => (id === 'virtual:brand.css' ? '\0virtual:brand.css' : undefined),
    load: (id) => (id === '\0virtual:brand.css' ? brandCss : undefined),
    configureServer(server) {
      for (const [name, file] of Object.entries(generated)) {
        server.middlewares.use(`/${name}`, (_req, res) => {
          res.setHeader('Content-Type', file.type)
          res.setHeader('Cache-Control', 'no-cache')
          res.end(file.body())
        })
      }
    },
    generateBundle(options) {
      // Only the client build ships files (vite-ssg also runs an SSR build into a temp folder).
      if (options.format !== 'es' || this.environment?.config.build.ssr) return
      for (const [name, file] of Object.entries(generated)) {
        this.emitFile({ type: 'asset', fileName: name, source: file.body() })
      }
    },
  }
}

export default defineConfig(({ mode }) => {
  const env = buildEnv(mode)
  const edition = resolveEdition(env)
  const brand = JSON.parse(readFileSync(`${editionsDir}/${edition}/brand.json`, 'utf8')) as {
    name: string
    hue: number
  }
  const siteEnv: SiteEnv = Object.fromEntries(siteEnvKeys.map((key) => [key, env[key]]))
  const site = resolveSiteConfig(siteEnv, { id: edition, brand })
  return {
    plugins: [vue(), tailwindcss(), landingShell(edition, site)],
    define: { __SITE__: JSON.stringify(site) },
    resolve: {
      alias: {
        '@edition': `${editionsDir}/${edition}/index.ts`,
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: { port: 5174, strictPort: true },
    preview: { port: 4174, strictPort: true },
    ssgOptions: {
      // /bot -> bot.html, /de -> de.html; Caddy serves `{path}.html` (deploy/caddy/landing.Caddyfile).
      dirStyle: 'flat',
      // No critical-CSS inlining: the CSP forbids inline styles.
      beastiesOptions: false,
      formatting: 'none',
      includedRoutes: () => allPaths(),
      // The SSR manifest is a build input only; it names source paths and must not be served.
      onFinished: () =>
        rmSync(fileURLToPath(new URL('./dist/.vite', import.meta.url)), {
          recursive: true,
          force: true,
        }),
    },
    test: {
      environment: 'jsdom',
      include: ['src/**/*.spec.ts'],
    },
  }
})
