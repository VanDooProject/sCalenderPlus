import { expect, test } from '@playwright/test'

test('app shell renders and reports the mocked API as reachable', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByTestId('app-title')).toHaveText('sCalenderPlus')
  // `/config.json` and `/health/live` come from MSW (no backend running).
  await expect(page.getByTestId('environment-badge')).toHaveText('Environment: mock')
  await expect(page.getByTestId('api-status')).toHaveText('reachable')
})
