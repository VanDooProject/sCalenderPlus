import { expect, test } from '@playwright/test'
import { expectAccessible } from '../support/a11y'
import { groups, mock, startSignedOut } from './fixtures'

const signedOutPages = [
  ['login', '/login', 'Sign in'],
  ['register', '/register', 'Create your account'],
  ['forgot password', '/forgot-password', 'Reset your password'],
  [
    'reset password',
    `/reset-password?userId=${mock.userId}&token=${mock.linkToken}`,
    'Choose a new password',
  ],
  [
    'verify email',
    `/verify-email?userId=${mock.userId}&token=${mock.linkToken}`,
    'Email address confirmed',
  ],
  ['invite (signed out)', `/invite?token=${groups.inviteToken}`, 'Join Choir'],
] as const

const signedInPages = [
  ['calendar', '/calendar', 'Calendar'],
  ['groups', '/groups', 'Groups'],
  ['profile', '/settings/profile', 'Settings'],
  ['security', '/settings/security', 'Settings'],
  ['group members', `/groups/${groups.lions}/members`, 'FC Lions'],
  ['group invites', `/groups/${groups.lions}/invites`, 'FC Lions'],
  ['group settings', `/groups/${groups.lions}/settings`, 'FC Lions'],
  ['invite (signed in)', `/invite?token=${groups.inviteToken}`, 'Join Choir'],
] as const

for (const scheme of ['light', 'dark'] as const) {
  test.describe(`${scheme} theme`, () => {
    test.use({ colorScheme: scheme })

    for (const [name, path, heading] of signedOutPages) {
      test(`${name} has no serious a11y violations`, async ({ page }) => {
        await startSignedOut(page)
        await page.goto(path)
        await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible()
        await expectAccessible(page, name)
      })
    }

    for (const [name, path, heading] of signedInPages) {
      test(`${name} has no serious a11y violations`, async ({ page }) => {
        await page.goto(path)
        await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible()
        await expectAccessible(page, name)
      })
    }

    test('login with errors and the 2FA step have no serious a11y violations', async ({ page }) => {
      await startSignedOut(page)
      await page.goto('/login')
      await page.getByRole('button', { name: 'Sign in' }).click()
      await expect(page.getByTestId('field-error').first()).toBeVisible()
      await expectAccessible(page, 'login with errors')

      await page.getByLabel('Email address').fill(mock.twoFactorEmail)
      await page.getByLabel('Password', { exact: true }).fill(mock.password)
      await page.getByRole('button', { name: 'Sign in' }).click()
      await expect(page.getByLabel('Authentication code')).toBeVisible()
      await expectAccessible(page, '2FA step')
    })

    test('group dialogs and menus have no serious a11y violations', async ({ page }) => {
      await page.goto('/groups')
      await page.getByRole('button', { name: 'New group' }).click()
      await expect(page.getByRole('dialog', { name: 'Create a group' })).toBeVisible()
      await expectAccessible(page, 'create group dialog')
      await page.keyboard.press('Escape')

      await page.goto(`/groups/${groups.lions}/members`)
      await page.getByRole('button', { name: 'Actions for Max' }).click()
      await expect(page.getByRole('menu')).toBeVisible()
      await expectAccessible(page, 'member menu')
      await page.getByRole('menuitem', { name: 'Remove from group' }).click()
      await expect(page.getByRole('dialog', { name: 'Remove Max?' })).toBeVisible()
      await expectAccessible(page, 'remove member dialog')
    })

    test('2FA setup and open menus have no serious a11y violations', async ({ page }) => {
      await page.goto('/settings/security')
      await page.getByRole('button', { name: 'Set up two-factor authentication' }).click()
      await expect(page.getByTestId('qr-code')).toBeVisible()
      await expectAccessible(page, '2FA setup')

      await page.getByTestId('user-menu-trigger').click()
      await expect(page.getByRole('menu')).toBeVisible()
      await expectAccessible(page, 'user menu')
    })
  })
}
