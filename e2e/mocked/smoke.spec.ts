import { expect, test } from '@playwright/test'

test('app shell renders with the mocked API', async ({ page }) => {
  await page.goto('/')

  // Mock mode starts signed in: `/` opens the calendar inside the shell.
  await expect(page).toHaveURL(/\/calendar$/)
  await expect(page.getByTestId('app-title')).toHaveText('sCalenderPlus')
  // `/config.json`, `/api/v1/me` and `/api/v1/calendars` come from MSW (no backend running).
  await expect(page.getByTestId('environment-badge')).toHaveText('Environment: mock')
  await expect(page.getByTestId('sidebar-calendars')).toContainText('FC Lions – Club')
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Calendar')
})

test.describe('with a German browser', () => {
  test.use({ locale: 'de-DE' })

  test('renders German and sets the document language from the start', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByTestId('environment-badge')).toHaveText('Umgebung: mock')
    await expect(page.locator('html')).toHaveAttribute('lang', 'de')
    await expect(page).toHaveTitle('Kalender · sCalenderPlus')
  })
})
