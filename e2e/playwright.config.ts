import { defineConfig, devices } from '@playwright/test'

/**
 * Two projects (docs/development/workflow.md §6):
 * - `mocked`: the app on the Vite dev server in mock mode (`pnpm --filter app dev:mock`), MSW answers
 *   API requests with the shared handlers from @scalenderplus/api-client. Started by `webServer`.
 * - `fullstack`: the docker compose stack (deploy/docker-compose.yml), already running at
 *   E2E_FULLSTACK_BASE_URL (default http://localhost:8080).
 *
 * Local runs may point to an installed Chromium whose revision differs from this Playwright
 * version with PLAYWRIGHT_CHROMIUM_EXECUTABLE (CI runs `playwright install chromium` instead).
 */
const isCI = !!process.env.CI
const mockedPort = Number(process.env.E2E_MOCKED_PORT ?? 5173)
const mockedBaseURL = `http://localhost:${mockedPort}`
const fullstackBaseURL = process.env.E2E_FULLSTACK_BASE_URL ?? 'http://localhost:8080'
const executablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined

/** The Vite server is only needed when the `mocked` project runs (all projects, or selected). */
function selectedProjects(argv: readonly string[]): string[] {
  const names: string[] = []
  argv.forEach((arg, i) => {
    if (arg === '--project' && argv[i + 1]) names.push(argv[i + 1]!)
    else if (arg.startsWith('--project=')) names.push(arg.slice('--project='.length))
  })
  return names
}
const projects = selectedProjects(process.argv)
const needsMockedServer = projects.length === 0 || projects.includes('mocked')

export default defineConfig({
  forbidOnly: isCI,
  retries: isCI ? 1 : 0,
  workers: isCI ? 2 : undefined,
  reporter: isCI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  use: {
    ...devices['Desktop Chrome'],
    launchOptions: { executablePath },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'mocked',
      testDir: './mocked',
      use: { baseURL: mockedBaseURL },
    },
    {
      name: 'fullstack',
      testDir: './fullstack',
      use: { baseURL: fullstackBaseURL },
    },
  ],
  webServer: needsMockedServer
    ? {
        command: `pnpm -C ../frontend --filter app dev:mock --port ${mockedPort} --strictPort`,
        url: mockedBaseURL,
        reuseExistingServer: !isCI,
        timeout: 120_000,
        stdout: 'ignore',
        stderr: 'pipe',
      }
    : undefined,
})
