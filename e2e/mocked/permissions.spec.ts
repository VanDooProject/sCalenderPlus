import { expect, test, type Page } from '@playwright/test'
import { expectAccessible } from '../support/a11y'

/**
 * Event permissions against the stateful MSW override mocks
 * (frontend/packages/api-client/src/mocks/overrides.ts): "Dentist" is private, the league match is
 * shared with Max to edit, "Training" (2 Nov 2026) is the mock user's own event through the
 * creator floor — sharing it with Olga (book club, outside FC Lions) is external sharing.
 */
const event = (page: Page, title: string | RegExp) =>
  page.getByTestId('calendar-event').filter({ hasText: title })

async function openPermissions(page: Page, title: string) {
  await event(page, title).first().click()
  await page.getByTestId('event-tab-permissions').click()
  return page.getByTestId('event-permissions')
}

test('shows custom permissions in the views and the private preset of "Dentist"', async ({
  page,
}) => {
  await page.goto('/calendar?view=week')
  const dentist = event(page, 'Dentist')
  await expect(dentist.getByTestId('event-custom-permissions')).toHaveAttribute(
    'aria-label',
    'Custom permissions',
  )
  await expect(event(page, 'Lunch with Max').getByTestId('event-custom-permissions')).toHaveCount(0)
  const editor = await openPermissions(page, 'Dentist')
  await expect(editor).toContainText('always keep full access')
  await expect(editor.getByTestId('override-everyone')).toContainText('Everyone in this calendar')
  await expect(editor.getByTestId('override-level-everyone')).toContainText('Hidden')
  await expectAccessible(page, 'event permissions')
})

test('applies a preset, adds a share and shows violations next to the entry', async ({ page }) => {
  await page.goto('/calendar?view=week&date=2026-11-02')
  const editor = await openPermissions(page, 'Training')
  await expect(editor.getByTestId('override-level-everyone')).toContainText('Busy only')

  // "Private": everyone → Hidden; Vic's explicit entry stays.
  await editor.getByTestId('preset-private').click()
  await expect(editor.getByTestId('override-level-everyone')).toContainText('Hidden')
  await editor.getByTestId('overrides-save').click()
  await expect(page.getByTestId('toast').getByText('Permissions saved.')).toBeVisible()
  await expect(editor.getByTestId('overrides-save')).toBeDisabled()

  // Share with Max (FC Lions): allowed.
  await editor.getByTestId('override-add-principal').click()
  await page.getByRole('option', { name: 'Max' }).click()
  await editor.getByTestId('override-add').click()
  await expect(editor.getByTestId('overrides')).toContainText('Max')
  await editor.getByTestId('overrides-save').click()
  await expect(editor.getByTestId('overrides-save')).toBeDisabled()

  // Share with Olga (outside the calendar): refused, the entry names the reason.
  await editor.getByTestId('override-add-principal').click()
  await page.getByRole('option', { name: 'Olga' }).click()
  await editor.getByTestId('override-add').click()
  await editor.getByTestId('overrides-save').click()
  await expect(editor.getByTestId('form-error')).toContainText('outside the calendar')
  const violation = editor.getByTestId('override-violation')
  await expect(violation).toHaveCount(1)
  await expect(violation).toContainText('only calendar managers may share')
  await expect(
    editor.locator('li').filter({ has: page.getByTestId('override-violation') }),
  ).toContainText('Olga')
  await expectAccessible(page, 'event permissions with violations')

  // Removing the entry makes the set valid again.
  await editor.getByRole('button', { name: 'Remove Olga' }).click()
  await expect(editor.getByTestId('override-violation')).toHaveCount(0)
  await expect(editor.getByTestId('overrides-save')).toBeDisabled()
})

test('explains another person’s access with the decisive step', async ({ page }) => {
  await page.goto('/calendar?view=week')
  await event(page, 'League match').first().click()
  await page.getByTestId('event-tab-access').click()
  const panel = page.getByTestId('event-explain')
  await expect(panel.getByTestId('explain-result')).toContainText('Your access: Full control')
  await panel.getByTestId('explain-person').click()
  await page.getByRole('option', { name: 'Max' }).click()
  await expect(panel.getByTestId('explain-result')).toHaveText('Max: Can edit')
  await expect(panel.getByTestId('explain-decisive')).toContainText(
    'The most specific custom permission (Max) applies: Can edit.',
  )
  await expect(panel).toContainText('Member of FC Lions as Member')
  await expectAccessible(page, 'explain panel')
})

test('readers see no editor, only their own level and why', async ({ page }) => {
  await page.goto('/calendar?view=week')
  await event(page, 'Book club').first().click()
  const dialog = page.getByTestId('event-details')
  await expect(dialog.getByTestId('event-details-level')).toContainText('You can view this event')
  await expect(page.getByTestId('event-tab-permissions')).toHaveCount(0)
  await dialog.getByTestId('event-details-why').click()
  const panel = page.getByTestId('event-explain')
  await expect(panel.getByTestId('explain-person')).toHaveCount(0)
  await expect(panel.getByTestId('explain-result')).toHaveText('Your access: Can view')
  await expect(panel.getByTestId('explain-decisive')).toContainText('From the calendar: Can view.')
})
