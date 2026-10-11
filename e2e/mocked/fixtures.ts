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

/**
 * Groups of the stateful MSW group mocks (frontend/packages/api-client/src/mocks/groups.ts): the
 * mock user owns "FC Lions" (members Adam admin, Max member, Vic viewer) and is a member of
 * "Book club"; `inviteToken` is a link to "Choir". Mock state lives in the page: a reload resets it.
 */
export const groups = {
  lions: '0192f2c4-0000-7000-8000-000000000101',
  bookClub: '0192f2c4-0000-7000-8000-000000000102',
  choir: '0192f2c4-0000-7000-8000-000000000103',
  adam: '0192f2c4-0000-7000-8000-000000000002',
  max: '0192f2c4-0000-7000-8000-000000000003',
  vic: '0192f2c4-0000-7000-8000-000000000004',
  inviteToken: 'mock-invite-token',
} as const

/** Mock mode starts signed in; this makes every page load of `page` start signed out. */
export async function startSignedOut(page: Page) {
  await page.addInitScript(() => localStorage.setItem('scal.mock.session', 'signed-out'))
}

/**
 * Calendars of the stateful MSW calendar mocks (frontend/packages/api-client/src/mocks/calendars.ts):
 * the mock user owns "FC Lions – Club" (group calendar, a grant to Eve: edit), "Mia" (personal),
 * reads "Book club" and owns the frozen "Old team" (a grant to Adam: edit).
 */
export const calendars = {
  lions: '0192f2c4-0000-7000-8000-000000000301',
  mia: '0192f2c4-0000-7000-8000-000000000302',
  bookClub: '0192f2c4-0000-7000-8000-000000000303',
  frozen: '0192f2c4-0000-7000-8000-000000000305',
  eve: '0192f2c4-0000-7000-8000-000000000005',
  planLimitName: 'Over the limit',
} as const
