import type { Page } from '@playwright/test'

/**
 * Inputs the stateful MSW auth mocks accept (frontend/packages/api-client/src/mocks/auth.ts,
 * `mockCredentials`); duplicated here because e2e does not depend on the frontend workspace.
 */
export const mock = {
  email: 'mia@example.test',
  password: 'correct-horse-42',
  twoFactorEmail: 'two-factor@example.test',
  rateLimitedEmail: 'limited@example.test',
  totpCode: '123456',
  recoveryCode: 'ABCDE-12345',
  linkToken: 'mock-link-token',
  userId: '0192f2c4-0000-7000-8000-000000000001',
} as const

/** Mock mode starts signed in; this makes every page load of `page` start signed out. */
export async function startSignedOut(page: Page) {
  await page.addInitScript(() => localStorage.setItem('scal.mock.session', 'signed-out'))
}
