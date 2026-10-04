import { expect, test } from '@playwright/test'
import { linkTo, waitForEmail } from './mailpit'

test('sign up, confirm the email from Mailpit, sign in and sign out', async ({ page, request }) => {
  const email = `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@example.test`
  const password = 'e2e correct horse battery'

  // Register: never signs in, always "check your email".
  await page.goto('/register')
  await page.getByLabel('Name').fill('E2E Tester')
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible()

  // The confirmation link from the email (sent by the worker through Mailpit).
  const body = await waitForEmail(request, email, /confirm your email/i)
  await page.goto(linkTo(body, '/verify-email'))
  await expect(page.getByRole('heading', { name: 'Email address confirmed' })).toBeVisible()

  // Sign in.
  await page.getByRole('link', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/login$/)
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page).toHaveURL(/\/calendar$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Calendar' })).toBeVisible()
  // Confirmed: no verification banner; the profile shows the verified address.
  await expect(page.getByTestId('verify-banner')).toHaveCount(0)
  await page.goto('/settings/profile')
  await expect(page.getByTestId('profile-email')).toHaveText(email)

  // Profile changes go through If-Match with the ETag of `GET /me`.
  await page.getByTestId('profile-name').fill('E2E Renamed')
  await page.getByLabel('Time zone', { exact: true }).fill('vienna')
  await page.getByRole('option', { name: /Europe\/Vienna/ }).click()
  await page.getByRole('button', { name: 'Save changes' }).click()
  await expect(page.getByTestId('toast')).toContainText('Profile saved.')
  await page.reload()
  await expect(page.getByTestId('profile-name')).toHaveValue('E2E Renamed')
  await expect(page.getByLabel('Time zone', { exact: true })).toHaveValue('Europe/Vienna')

  // Sign out: the session is gone, protected pages lead to the login again.
  await page.getByTestId('user-menu-trigger').click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()
  await expect(page).toHaveURL(/\/login$/)
  await page.goto('/settings/profile')
  await expect(page).toHaveURL(/\/login\?next=/)
})
