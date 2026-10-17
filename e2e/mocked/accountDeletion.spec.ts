import { expect, test } from '@playwright/test'
import { expectAccessible } from '../support/a11y'
import { groups, mock } from './fixtures'

/**
 * Account deletion in the security settings against the MSW account mock (frontend/packages/
 * api-client/src/mocks/account.ts): confirm with the password, the grace period with a banner on
 * every page, cancel; and the blocked case (`localStorage['scal.mock.account'] = 'blocked'`).
 * Mock state lives in the page: a reload resets it.
 */

test('schedule the deletion with the password, see it everywhere, cancel it', async ({ page }) => {
  await page.goto('/settings/security')
  const card = page.getByTestId('account-deletion')
  await expect(card).toContainText('Your calendar subscriptions stop at once.')
  await page.getByRole('button', { name: 'Delete account' }).click()

  const dialog = page.getByRole('dialog', { name: 'Delete your account?' })
  await dialog.getByLabel('Current password').fill('not the password')
  await dialog.getByRole('button', { name: 'Delete account' }).click()
  await expect(dialog.getByTestId('form-error')).toContainText('The password or code is incorrect.')
  await dialog.getByLabel('Current password').fill(mock.password)
  await dialog.getByRole('button', { name: 'Delete account' }).click()

  await expect(page.getByTestId('toast')).toContainText('scheduled for deletion')
  await expect(page.getByTestId('account-deletion-scheduled')).toContainText(
    'Your account will be deleted on',
  )
  await page.getByTestId('nav-calendar').click() // client-side: a reload would reset the mocks
  const banner = page.getByTestId('account-deletion-banner')
  await expect(banner).toContainText('Your account will be deleted on')
  await banner.getByRole('link', { name: 'Cancel deletion' }).click()

  await expect(page).toHaveURL(/\/settings\/security$/)
  await page.getByRole('button', { name: 'Cancel deletion' }).click()
  await expect(page.getByTestId('toast').filter({ hasText: 'Deletion cancelled' })).toBeVisible()
  await expect(page.getByTestId('account-deletion-banner')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Delete account' })).toBeEnabled()
})

test('a sole owner sees what to hand over first', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('scal.mock.account', 'blocked'))
  await page.goto('/settings/security')

  const blockers = page.getByTestId('account-deletion-blocker')
  await expect(blockers).toHaveCount(2)
  await expect(blockers.first()).toContainText('Only owner, 3 other members')
  await expect(blockers.last()).toContainText('shared with 2 people or groups')
  await expect(page.getByRole('button', { name: 'Delete account' })).toBeDisabled()
  await expectAccessible(page, 'deletion blockers')
  await blockers.first().getByRole('link', { name: 'FC Lions' }).click()
  await expect(page).toHaveURL(new RegExp(`/groups/${groups.lions}/members$`))
})
