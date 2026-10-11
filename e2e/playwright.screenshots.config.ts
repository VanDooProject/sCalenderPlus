import { defineConfig } from '@playwright/test'

/**
 * Screenshot gallery (docs/screenshots.md): `pnpm -C e2e screenshots`.
 *
 * Separate from playwright.config.ts on purpose, so the `mocked`/`fullstack` CI projects never pick
 * it up. Runs the app in mock mode like the `mocked` project and writes deterministic images into
 * docs/screenshots/ (fixed viewport, locale, time zone, clock and reduced motion).
 */
const port = Number(process.env.E2E_SCREENSHOTS_PORT ?? 5174)
const baseURL = `http://localhost:${port}`
const executablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined

export default defineConfig({
  testDir: './screenshots',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL,
    browserName: 'chromium',
    launchOptions: { executablePath },
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1,
    locale: 'en-GB',
    timezoneId: 'Europe/Berlin',
    colorScheme: 'light',
    reducedMotion: 'reduce',
  },
  webServer: {
    command: `pnpm -C ../frontend --filter app dev:mock --port ${port} --strictPort`,
    url: baseURL,
    reuseExistingServer: true,
    timeout: 120_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
})
