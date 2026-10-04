import { expect, test } from '@playwright/test'
import { mock, startSignedOut } from './fixtures'

test.beforeEach(async ({ page }) => {
  await startSignedOut(page)
})

test('a protected page leads to the login and back after signing in', async ({ page }) => {
  await page.goto('/settings/profile')
  await expect(page).toHaveURL('/login?next=/settings/profile')

  await page.getByLabel('Email address').fill(mock.email)
  await page.getByLabel('Password', { exact: true }).fill(mock.password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page).toHaveURL('/settings/profile')
  await expect(page.getByTestId('profile-email')).toHaveText(mock.email)
})

test('wrong credentials and validation are reported accessibly', async ({ page }) => {
  await page.goto('/login')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByTestId('login-email')).toBeFocused()
  await expect(page.getByLabel('Email address')).toHaveAttribute('aria-invalid', 'true')

  await page.getByLabel('Email address').fill(mock.email)
  await page.getByLabel('Password', { exact: true }).fill('not the password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('alert')).toHaveText('Email address or password is incorrect.')
})

test('a rate-limited login says how long to wait', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('Email address').fill(mock.rateLimitedEmail)
  await page.getByLabel('Password', { exact: true }).fill(mock.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('alert')).toHaveText(
    'Too many attempts. Please try again in 42 seconds.',
  )
})

test('login with two-factor authentication (authenticator code)', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('Email address').fill(mock.twoFactorEmail)
  await page.getByLabel('Password', { exact: true }).fill(mock.password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('heading', { name: 'Two-factor authentication' })).toBeVisible()
  await expect(page.getByLabel('Authentication code')).toBeFocused()
  await page.getByLabel('Authentication code').fill('000000')
  await page.getByRole('button', { name: 'Verify' }).click()
  await expect(page.getByRole('alert')).toHaveText('Email address or password is incorrect.')

  await page.getByLabel('Authentication code').fill(mock.totpCode)
  await page.getByRole('button', { name: 'Verify' }).click()
  await expect(page).toHaveURL('/calendar')
  await expect(page.getByTestId('user-menu-trigger')).toBeVisible()
})

test('login with two-factor authentication (recovery code)', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('Email address').fill(mock.twoFactorEmail)
  await page.getByLabel('Password', { exact: true }).fill(mock.password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  await page.getByRole('button', { name: 'Use a recovery code instead' }).click()
  await page.getByLabel('Recovery code').fill(mock.recoveryCode)
  await page.getByRole('button', { name: 'Verify' }).click()
  await expect(page).toHaveURL('/calendar')
})

test('register shows "check your email" and leads to the login', async ({ page }) => {
  await page.goto('/login')
  await page.getByRole('link', { name: 'Create an account' }).click()
  await expect(page.getByRole('heading', { name: 'Create your account' })).toBeVisible()

  await page.getByLabel('Name').fill('Noah')
  await page.getByLabel('Email address').fill('noah@example.test')
  await page.getByLabel('Password', { exact: true }).fill('short')
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByTestId('field-error')).toHaveText('Use at least 10 characters.')

  await page.getByLabel('Password', { exact: true }).fill('a long passphrase')
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible()
  await expect(page.getByTestId('register-done')).toContainText('noah@example.test')
  await page.getByRole('link', { name: 'Continue to sign in' }).click()
  await expect(page).toHaveURL('/login')
})

test('forgot and reset password', async ({ page }) => {
  await page.goto('/login')
  await page.getByRole('link', { name: 'Forgot password?' }).click()
  await expect(page.getByRole('heading', { name: 'Reset your password' })).toBeVisible()
  await page.getByLabel('Email address').fill(mock.email)
  await page.getByRole('button', { name: 'Send reset link' }).click()
  await expect(page.getByTestId('forgot-done')).toContainText(mock.email)

  // The link from the email.
  await page.goto(`/reset-password?userId=${mock.userId}&token=${mock.linkToken}`)
  await page.getByLabel('New password', { exact: true }).fill('a new passphrase')
  await page.getByLabel('Repeat the new password').fill('a new passphrase')
  await page.getByRole('button', { name: 'Set new password' }).click()
  await expect(page.getByRole('heading', { name: 'Password changed' })).toBeVisible()
  await page.getByRole('link', { name: 'Sign in' }).click()
  await expect(page).toHaveURL('/login')
})

test('an used reset link explains what to do', async ({ page }) => {
  await page.goto(`/reset-password?userId=${mock.userId}&token=used`)
  await page.getByLabel('New password', { exact: true }).fill('a new passphrase')
  await page.getByLabel('Repeat the new password').fill('a new passphrase')
  await page.getByRole('button', { name: 'Set new password' }).click()
  await expect(page.getByRole('heading', { name: "This link doesn't work" })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Request a new link' })).toBeVisible()
})

test('verify email from the link', async ({ page }) => {
  await page.goto(`/verify-email?userId=${mock.userId}&token=${mock.linkToken}`)
  await expect(page.getByRole('heading', { name: 'Email address confirmed' })).toBeVisible()
})
