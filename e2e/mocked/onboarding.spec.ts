import { expect, test, type Locator, type Page } from '@playwright/test'
import { expectAccessible } from '../support/a11y'

/**
 * The first-run checklist (features.md §5 "Onboarding checklist") against the MSW mocks in their new-account
 * scenario (`scal.mock.scenario = new-user`: no calendars, groups or feed links). Roadmap AC: a new
 * user completes it in under two minutes; the flow is measured here in user interactions (clicks
 * and typed fields, the checklist's own and those in the dialogs it opens) and wall time.
 */

/** Interaction budget: a new user should get through in well under 15 clicks and fields. */
const maxInteractions = 15

async function startAsNewUser(page: Page) {
  await page.addInitScript(() => localStorage.setItem('scal.mock.scenario', 'new-user'))
}

/** Counts what a user does (each click or filled field is one interaction). */
function interactions() {
  let count = 0
  return {
    click: async (target: Locator) => {
      count += 1
      await target.click()
    },
    fill: async (target: Locator, value: string) => {
      count += 1
      await target.fill(value)
    },
    get count() {
      return count
    },
  }
}

const step = (page: Page, name: string) => page.getByTestId(`onboarding-step-${name}`)

test('a new user completes the checklist in a handful of steps', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await startAsNewUser(page)
  await page.goto('/calendar')
  const checklist = page.getByRole('region', { name: 'Get started' })
  await expect(checklist).toBeVisible()
  await expect(checklist.getByRole('progressbar', { name: 'Setup progress' })).toHaveAttribute(
    'aria-valuenow',
    '0',
  )
  await expect(checklist.getByRole('listitem')).toHaveCount(4)

  const user = interactions()
  const started = Date.now()

  // 1. First calendar: the name is suggested, the checklist stays on the page.
  await user.click(checklist.getByRole('button', { name: 'Create calendar' }))
  const calendarDialog = page.getByRole('dialog', { name: 'New calendar' })
  await expect(calendarDialog.getByLabel('Name')).toHaveValue('Mia')
  await user.click(calendarDialog.getByRole('button', { name: 'Create calendar' }))
  await expect(step(page, 'calendar')).toHaveAttribute('data-done', 'true')
  await expect(page).toHaveURL(/\/calendar/)

  // 2. A group and its invite link (with a QR code to show around).
  await user.click(checklist.getByRole('button', { name: 'Create group' }))
  const groupDialog = page.getByRole('dialog', { name: 'Create a group' })
  await user.fill(groupDialog.getByLabel('Name'), 'Climbing crew')
  await user.click(groupDialog.getByRole('button', { name: 'Create group' }))
  await expect(page).toHaveURL(/\/groups\/[^/]+\/invites$/)
  await user.click(page.getByRole('button', { name: 'Create link' }))
  await expect(page.getByTestId('invite-link-url')).toHaveValue(/\/invite\?token=/)
  await expect(page.getByRole('img', { name: 'QR code of the invite link' })).toBeVisible()
  await expect(step(page, 'group')).toHaveAttribute('data-done', 'true')

  // 3. One feed link of everything, for the calendar app.
  await user.click(checklist.getByRole('button', { name: 'Get feed link' }))
  const feedDialog = page.getByRole('dialog', { name: 'New feed link' })
  await user.click(feedDialog.getByRole('button', { name: 'Create link' }))
  const reveal = page.getByRole('dialog', { name: 'Your feed link' })
  await expect(reveal.getByRole('link', { name: 'Open in calendar app' })).toHaveAttribute(
    'href',
    /^webcal:/,
  )
  await user.click(reveal.getByRole('button', { name: 'Done' }))
  await expect(checklist.getByTestId('onboarding-progress')).toHaveText('3 of 3 done')
  await expect(checklist.getByRole('heading', { name: "You're all set" })).toBeVisible()
  const required = user.count

  // 4. Optional: an event with a cover title (what busy-only viewers see).
  await user.click(checklist.getByRole('link', { name: 'Add event' }))
  const eventDialog = page.getByTestId('event-dialog')
  await user.fill(eventDialog.getByTestId('event-title'), 'Dentist')
  await user.fill(eventDialog.getByTestId('event-cover-title'), 'Appointment')
  await user.click(eventDialog.getByTestId('event-save'))
  await expect(step(page, 'event')).toHaveAttribute('data-done', 'true')

  const seconds = (Date.now() - started) / 1000
  test.info().annotations.push({
    type: 'onboarding',
    description: `required steps: ${required} interactions; with the optional event: ${user.count} interactions; ${seconds.toFixed(1)} s automated`,
  })
  console.log(
    `[onboarding] required: ${required} interactions, all: ${user.count} interactions, ${seconds.toFixed(1)} s`,
  )
  expect(required).toBeLessThanOrEqual(10)
  expect(user.count).toBeLessThan(maxInteractions)
  // Automated time is no usability test, but the flow must not wait on anything slow.
  expect(seconds).toBeLessThan(30)

  // Done: closing it is for good.
  await checklist.getByTestId('onboarding-close').click()
  await expect(checklist).toBeHidden()
  await page.reload()
  await expect(page.getByTestId('calendar-page')).toBeVisible()
  await expect(page.getByTestId('onboarding')).toBeHidden()
})

test('the checklist can be hidden and reopened from the user menu', async ({ page }) => {
  await startAsNewUser(page)
  await page.goto('/groups')
  const checklist = page.getByRole('region', { name: 'Get started' })
  await expect(checklist).toBeVisible()
  // The calendar page has FullCalendar's own findings (see a11y.spec.ts): check on the groups page.
  await expectAccessible(page, 'onboarding checklist')
  await page.emulateMedia({ colorScheme: 'dark' })
  await expectAccessible(page, 'onboarding checklist (dark)')
  await checklist.getByRole('button', { name: 'Hide checklist' }).click()
  await expect(checklist).toBeHidden()
  await expect(page.getByTestId('toast')).toContainText('reopen it from your account menu')

  await page.reload()
  await expect(page.getByRole('heading', { name: 'Groups', level: 1 })).toBeVisible()
  await expect(checklist).toBeHidden()

  await page.getByTestId('user-menu-trigger').click()
  await page.getByRole('menuitem', { name: 'Getting started' }).click()
  await expect(checklist).toBeVisible()
  await expect(checklist.getByRole('heading', { name: 'Get started' })).toBeFocused()
  await expect(checklist.getByTestId('onboarding-progress')).toHaveText('0 of 3 done')
})

test('accounts that are set up already never see it', async ({ page }) => {
  await page.goto('/calendar')
  await expect(page.getByTestId('calendar')).toBeVisible()
  await expect(page.getByTestId('onboarding')).toBeHidden()
})
