import { expect, test } from '@playwright/test'

test('web serves the app and proxies a healthy api', async ({ page, request }) => {
  const ready = await request.get('/health/ready')
  expect(ready.status()).toBe(200)
  expect(await ready.json()).toMatchObject({ status: 'Healthy', checks: { database: 'Healthy' } })

  // The strict CSP must not break the app: collect violations and script errors. The session
  // probe (`GET /api/v1/me` → 401 while signed out) is logged by the browser and expected.
  const errors: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'error' && !/status of 401/.test(message.text())) {
      errors.push(message.text())
    }
  })
  page.on('pageerror', (error) => errors.push(error.message))

  const response = await page.goto('/')
  expect(response?.headers()['content-security-policy']).toContain("script-src 'self'")
  expect(response?.headers()['referrer-policy']).toBe('same-origin')

  // Signed out: the app sends the visitor to the login page.
  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByTestId('app-title')).toHaveText('sCalenderPlus')
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible()
  expect(errors).toEqual([])
})
