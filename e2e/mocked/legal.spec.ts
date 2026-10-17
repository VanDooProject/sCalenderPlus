import { expect, test } from '@playwright/test'
import { groups, startSignedOut } from './fixtures'

/**
 * Legal links on every page (built-in template pages unless /config.json names the deployment's
 * own).
 */

const appPages = ['/calendar', '/groups', `/groups/${groups.lions}/members`, '/settings/profile']

for (const path of appPages) {
  test(`legal links on ${path}`, async ({ page }) => {
    await page.goto(path)
    const legal = page.getByRole('navigation', { name: 'Legal' })
    await expect(legal.getByRole('link', { name: 'Imprint' })).toHaveAttribute(
      'href',
      '/legal/imprint',
    )
    await expect(legal.getByRole('link', { name: 'Privacy' })).toHaveAttribute(
      'href',
      '/legal/privacy',
    )
    await expect(legal.getByRole('link', { name: 'Terms' })).toHaveAttribute('href', '/legal/terms')
  })
}

test('sign-up links the terms; the template says it is one', async ({ page }) => {
  await startSignedOut(page)
  await page.goto('/register')
  await page.getByTestId('register-terms').click()
  await expect(page.getByRole('heading', { level: 1, name: 'Terms of use' })).toBeVisible()
  await expect(page.getByTestId('legal-template')).toContainText('PUBLIC_BRAND_TERMS_URL')
})

test('configured legal pages are linked directly', async ({ page }) => {
  // Mock mode serves this as /config.json's `brand` (brand.spec.ts).
  await page.addInitScript(() =>
    localStorage.setItem(
      'scal.mock.brand',
      JSON.stringify({ imprintUrl: 'https://example.com/imprint' }),
    ),
  )
  await page.goto('/calendar')
  await expect(
    page.getByRole('navigation', { name: 'Legal' }).getByRole('link', { name: 'Imprint' }),
  ).toHaveAttribute('href', 'https://example.com/imprint')
})
