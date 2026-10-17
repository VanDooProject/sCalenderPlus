import { expect, test } from '@playwright/test'
import { calendars, groups } from './fixtures'

/**
 * Contextual upgrade dialogs for `402 plan_limit_reached` (docs/product/plans.md "Upgrade
 * triggers"): the limit's numbers and the plan's wording from the problem, "coming soon" instead
 * of a checkout during the beta, and "Not now" for 14 days.
 */

test('creating a calendar over the Free limit shows the numbers', async ({ page }) => {
  await page.goto('/calendar')
  await page.getByRole('button', { name: 'New calendar' }).click()
  const form = page.getByRole('dialog', { name: 'New calendar' })
  await form.getByLabel('Name').fill(calendars.planLimitName)
  await form.getByRole('button', { name: 'Create calendar' }).click()

  const paywall = page.getByRole('dialog', { name: 'Calendar limit reached' })
  await expect(paywall).toBeVisible()
  await expect(paywall.getByTestId('paywall-numbers')).toHaveText('3 of 3')
  await expect(paywall).toContainText('This is the limit of the Free plan.')
  await expect(paywall).toContainText('Upgrades are coming soon')
  await expect(paywall.getByRole('link')).toHaveCount(0)

  // "Not now": the same limit doesn't open the dialog again; the form still explains it.
  await paywall.getByRole('button', { name: 'Not now' }).click()
  await expect(paywall).toBeHidden()
  await form.getByRole('button', { name: 'Create calendar' }).click()
  await expect(form.getByTestId('form-error')).toContainText('See plans')
  await expect(page.getByTestId('paywall-dialog')).toHaveCount(0)
})

test('the reverse trial names its end and the Pro limit', async ({ page }) => {
  await page.goto('/groups')
  await page.getByTestId('create-group').first().click()
  await page.getByTestId('group-name').fill(groups.limitName)
  await page.getByTestId('create-group-submit').click()

  const paywall = page.getByRole('dialog', { name: 'Group limit reached' })
  await expect(paywall.getByTestId('paywall-numbers')).toHaveText('10 of 10')
  await expect(paywall).toContainText(
    'This is the limit of Pro, which applies during the trial until December 30, 2026.',
  )
  await paywall.getByRole('button', { name: 'OK' }).click()
  await expect(paywall).toBeHidden()
})

test('inviting into a full group', async ({ page }) => {
  await page.goto(`/groups/${groups.lions}/invites`)
  await page.getByTestId('invite-email').fill(groups.fullEmail)
  await page.getByTestId('invite-email-submit').click()

  const paywall = page.getByRole('dialog', { name: 'This group is full' })
  await expect(paywall.getByTestId('paywall-numbers')).toHaveText('15 of 15')
  await expect(paywall).toContainText('Pro has room for many more members per group.')
})
