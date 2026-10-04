import { expect, test } from '@playwright/test'

test('theme toggle switches to dark and survives a reload', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'light' })
  await page.goto('/calendar')
  await expect(page.locator('html')).not.toHaveClass(/dark/)

  await page.getByTestId('theme-toggle').click()
  await page.getByRole('menuitemradio', { name: 'Dark' }).click()
  await expect(page.locator('html')).toHaveClass(/dark/)
  const background = await page.evaluate(() => getComputedStyle(document.body).backgroundColor)

  await page.reload()
  await expect(page.locator('html')).toHaveClass(/dark/)
  expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor)).toBe(
    background,
  )

  await page.getByTestId('theme-toggle').click()
  await page.getByRole('menuitemradio', { name: 'System' }).click()
  await expect(page.locator('html')).not.toHaveClass(/dark/)
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(page.locator('html')).toHaveClass(/dark/)
})

test('language switch translates the app and is remembered', async ({ page }) => {
  await page.goto('/calendar')
  await page.getByTestId('locale-switcher').click()
  await page.getByRole('menuitemradio', { name: 'Deutsch' }).click()

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Kalender')
  await expect(page.locator('html')).toHaveAttribute('lang', 'de')
  await expect(page).toHaveTitle('Kalender · sCalenderPlus')

  await page.reload()
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Kalender')
})

test('sign out from the user menu', async ({ page }) => {
  await page.goto('/calendar')
  await page.getByTestId('user-menu-trigger').click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()
  await expect(page).toHaveURL('/login')
  await expect(page.getByTestId('toast')).toContainText("You're signed out.")
})

test.describe('on a phone', () => {
  test.use({ viewport: { width: 390, height: 844 } })

  test('navigation lives in a drawer', async ({ page }) => {
    await page.goto('/calendar')
    await expect(page.getByTestId('sidebar')).toBeHidden()

    await page.getByRole('button', { name: 'Open navigation' }).click()
    const drawer = page.getByRole('dialog', { name: 'Main navigation' })
    await expect(drawer).toBeVisible()
    await drawer.getByRole('link', { name: 'Settings' }).click()

    await expect(drawer).toBeHidden()
    await expect(page).toHaveURL('/settings/profile')
  })
})
