import { expect, test } from '@playwright/test'
import { startSignedOut } from './fixtures'

/**
 * White-label branding from /config.json. In mock mode the app serves `localStorage['scal.mock.brand']`
 * as its `brand` (frontend/app/src/mocks/browser.ts); in containers Caddy renders it from
 * PUBLIC_BRAND_* (deploy/caddy/config.json.tmpl).
 */
test('applies the brand from /config.json', async ({ page }) => {
  await startSignedOut(page)
  await page.addInitScript(() =>
    localStorage.setItem(
      'scal.mock.brand',
      JSON.stringify({
        name: 'Club Calendar',
        hue: 145,
        logoUrl: '/favicon.svg',
        privacyUrl: 'https://example.com/privacy',
        supportEmail: 'help@example.com',
      }),
    ),
  )
  await page.goto('/login')

  await expect(page.getByTestId('app-title')).toHaveText('Club Calendar')
  await expect(page).toHaveTitle('Sign in · Club Calendar')
  await expect(page.getByTestId('brand-logo')).toHaveAttribute('src', '/favicon.svg')
  expect(
    await page.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue('--scal-brand-hue').trim(),
    ),
  ).toBe('145')
  await expect(page.getByTestId('legal-privacy')).toHaveAttribute(
    'href',
    'https://example.com/privacy',
  )
  await expect(page.getByTestId('legal-support')).toHaveAttribute('href', 'mailto:help@example.com')
})
