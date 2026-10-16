import { expect, test, type Page } from '@playwright/test'
import { expectAccessible } from '../support/a11y'

/**
 * Creating and editing events against the stateful MSW event mocks
 * (frontend/packages/api-client/src/mocks/events.ts): the seed is anchored to the current week
 * (calendar.spec.ts); "Team practice" is a weekly series on Tuesdays, "Book club" is read-only.
 * Saving the title `Edited elsewhere` simulates a concurrent change once (412).
 */
const event = (page: Page, title: string | RegExp) =>
  page.getByTestId('calendar-event').filter({ hasText: title })

/** Clicks the slot at `time` on the `day`-th column (0 = Monday) of the week grid. */
async function clickSlot(page: Page, day: number, time: string) {
  const lane = page.locator(`[data-time="${time}"]`).last()
  await lane.scrollIntoViewIfNeeded()
  const column = page.locator('[data-date]').nth(day)
  const [x, y] = await Promise.all([column.boundingBox(), lane.boundingBox()])
  await page.mouse.click(x!.x + x!.width / 2, y!.y + y!.height / 2)
}

async function openEdit(page: Page, title: string) {
  await event(page, title).first().click()
  await page.getByTestId('event-details-edit').click()
  return page.getByTestId('event-dialog')
}

test('creates an event by clicking a slot', async ({ page }) => {
  await page.goto('/calendar?view=week')
  await expect(event(page, 'Lunch with Max')).toBeVisible()
  await clickSlot(page, 0, '10:00:00')
  const dialog = page.getByTestId('event-dialog')
  await expect(dialog.getByRole('heading', { name: 'New event' })).toBeVisible()
  await expect(page.getByTestId('event-start-time')).toHaveValue('10:00')
  await expect(page.getByTestId('event-end-time')).toHaveValue('10:30')

  // Validation: a title is required.
  await page.getByTestId('event-save').click()
  await expect(dialog).toContainText('Please fill in this field.')
  await page.getByTestId('event-title').fill('Sprint review')
  await page.getByTestId('event-end-time').fill('11:00')
  await page.getByTestId('event-location').fill('Room 4')
  await page.getByTestId('event-save').click()
  await expect(dialog).toBeHidden()
  await expect(event(page, 'Sprint review')).toContainText('10:00')

  await event(page, 'Sprint review').click()
  await expect(page.getByTestId('event-details')).toContainText('Room 4')
})

test('a cover title is shown to editors, changed and removed', async ({ page }) => {
  await page.goto('/calendar?view=week')
  // Seeded: the league match shows "Football" to busy-only viewers.
  await event(page, 'League match').click()
  const coverLine = page.getByTestId('event-details-cover-title')
  await expect(coverLine).toHaveText('Others who only see busy time see: Football')

  await page.getByTestId('event-details-edit').click()
  const dialog = page.getByTestId('event-dialog')
  const cover = page.getByTestId('event-cover-title')
  await expect(cover).toHaveValue('Football')
  await expect(cover).toHaveAccessibleDescription(/instead of “Busy”/)
  await cover.fill('Match day')
  await page.getByTestId('event-save').click()
  await expect(dialog).toBeHidden()
  await event(page, 'League match').click()
  await expect(coverLine).toHaveText('Others who only see busy time see: Match day')

  await page.getByTestId('event-details-edit').click()
  await cover.fill('')
  await page.getByTestId('event-save').click()
  await expect(dialog).toBeHidden()
  await event(page, 'League match').click()
  await expect(page.getByTestId('event-details')).toBeVisible()
  await expect(coverLine).toHaveCount(0)
})

test('n opens a new event; a time in a DST gap is reported after saving', async ({ page }) => {
  await page.goto('/calendar?view=week')
  await expect(event(page, 'Lunch with Max')).toBeVisible()
  await page.keyboard.press('n')
  const dialog = page.getByTestId('event-dialog')
  await expect(dialog).toBeVisible()
  await expect(page.getByTestId('event-title')).toBeFocused()
  await expectAccessible(page, 'event dialog')
  await page.keyboard.press('Escape')
  await expect(dialog).toBeHidden()

  await page.getByTestId('calendar-new-event').click()
  await page.getByTestId('event-title').fill('Night shift')
  await page.getByTestId('event-start-date').fill('2027-03-28')
  await page.getByTestId('event-start-time').fill('02:30')
  await page.getByTestId('event-end-date').fill('2027-03-28')
  await page.getByTestId('event-end-time').fill('05:00')
  await page.getByTestId('event-save').click()
  await expect(page.getByTestId('event-warnings')).toContainText(
    '02:30 does not exist on that day (clocks change); the event was saved at 03:30.',
  )
  await page.getByTestId('event-warnings-close').click()
  await expect(dialog).toBeHidden()
})

test('edits the title of an event', async ({ page }) => {
  await page.goto('/calendar?view=week')
  const dialog = await openEdit(page, 'Dentist')
  await expect(dialog.getByRole('heading', { name: 'Edit event' })).toBeVisible()
  await expect(page.getByTestId('event-title')).toHaveValue('Dentist')
  await page.getByTestId('event-title').fill('Dentist (check-up)')
  await page.getByTestId('event-save').click()
  await expect(dialog).toBeHidden()
  await expect(event(page, 'Dentist (check-up)')).toBeVisible()
})

test('a read-only event shows no edit button and says why', async ({ page }) => {
  await page.goto('/calendar?view=week')
  await event(page, 'Book club').click()
  await expect(page.getByTestId('event-details-readonly')).toBeVisible()
  await expect(page.getByTestId('event-details-edit')).toHaveCount(0)
})

test('a conflicting change reloads and keeps the edits', async ({ page }) => {
  await page.goto('/calendar?view=week')
  const dialog = await openEdit(page, 'League match')
  await page.getByTestId('event-title').fill('Edited elsewhere')
  await page.getByTestId('event-save').click()
  await expect(dialog).toContainText('Someone changed this in the meantime')
  await expect(page.getByTestId('event-title')).toHaveValue('Edited elsewhere')
  await page.getByTestId('event-save').click()
  await expect(dialog).toBeHidden()
  await expect(event(page, 'Edited elsewhere')).toBeVisible()
})

test('drags an event to another time', async ({ page }) => {
  await page.goto('/calendar?view=week')
  const dentist = event(page, 'Dentist')
  await expect(dentist).toContainText('8:30')
  await dentist.scrollIntoViewIfNeeded()
  const box = (await dentist.boundingBox())!
  const target = (await page.locator('[data-time="10:30:00"]').last().boundingBox())!
  await page.mouse.move(box.x + box.width / 2, box.y + 5)
  await page.mouse.down()
  await page.mouse.move(box.x + box.width / 2, box.y + 25, { steps: 5 })
  await page.mouse.move(box.x + box.width / 2, target.y + 5, { steps: 10 })
  await page.mouse.up()
  await expect(page.getByText('Event moved.').first()).toBeVisible()
  await expect(event(page, 'Dentist')).toContainText('10:30')
})

test('edits one occurrence of a series after asking which ones', async ({ page }) => {
  await page.goto('/calendar?view=week')
  const dialog = await openEdit(page, 'Team practice')
  await expect(page.getByTestId('event-rule')).toHaveText('Weekly, 12 times')
  await page.getByTestId('event-title').fill('Practice (indoor)')
  await page.getByTestId('event-save').click()
  const scope = page.getByTestId('event-scope')
  await expect(scope.getByRole('radio')).toHaveCount(3)
  await expect(page.getByTestId('event-scope-this')).toBeChecked()
  await page.getByTestId('event-save').click()
  await expect(dialog).toBeHidden()
  await expect(event(page, 'Practice (indoor)')).toBeVisible()
  // Only this occurrence changed.
  await page.getByTestId('calendar-next').click()
  await expect(event(page, 'Team practice')).toBeVisible()
})

test('deletes an event and cancels an occurrence', async ({ page }) => {
  await page.goto('/calendar?view=week')
  let dialog = await openEdit(page, 'Lunch with Max')
  await page.getByTestId('event-delete').click()
  await expect(dialog).toContainText('Delete “Lunch with Max”?')
  await page.getByTestId('event-delete-confirm').click()
  await expect(dialog).toBeHidden()
  await expect(event(page, 'Lunch with Max')).toHaveCount(0)

  dialog = await openEdit(page, 'Team practice')
  await page.getByTestId('event-delete').click()
  await page.getByTestId('event-delete-this').click()
  await expect(dialog).toBeHidden()
  await expect(event(page, 'Team practice')).toHaveCount(0)
  await page.getByTestId('calendar-next').click()
  await expect(event(page, 'Team practice')).toBeVisible()
})
