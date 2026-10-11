import { expect, test, type Page } from '@playwright/test'

/**
 * Calendar views against the stateful MSW calendar mocks
 * (frontend/packages/api-client/src/mocks/calendars.ts): the seed is anchored to the current week —
 * "Lunch with Max" (Mia, Monday), busy blocks of Adam's calendar (Wednesday, Friday), "Dentist"
 * (Mia, Thursday), "Parents’ evening" (shared with me), "Book club" (read-only, Friday) and the
 * "League match" (FC Lions – Club, Saturday).
 */
const calendars = {
  lions: '0192f2c4-0000-7000-8000-000000000301',
  mia: '0192f2c4-0000-7000-8000-000000000302',
} as const

const event = (page: Page, title: string | RegExp) =>
  page.getByTestId('calendar-event').filter({ hasText: title })

test('switches views and navigates with the toolbar and the keyboard', async ({ page }) => {
  await page.goto('/calendar?view=week')
  const title = page.getByTestId('calendar-title')
  await expect(event(page, 'Lunch with Max')).toBeVisible()
  await expect(page.getByTestId('calendar-today')).toBeDisabled()

  await page.getByTestId('calendar-view-month').click()
  await expect(page).toHaveURL(/view=month/)
  await expect(page.getByTestId('calendar-view-month')).toHaveAttribute('aria-checked', 'true')
  const month = await title.textContent()

  await page.getByTestId('calendar-next').click()
  await expect(title).not.toHaveText(month ?? '')
  await expect(page.getByTestId('calendar-today')).toBeEnabled()
  await page.getByTestId('calendar-today').click()
  await expect(title).toHaveText(month ?? '')

  await page.getByTestId('calendar-view-day').click()
  await expect(page).toHaveURL(/view=day/)

  // Shortcuts: a = agenda, n/p = next/previous, t = today.
  await page.keyboard.press('a')
  await expect(page.getByTestId('calendar-view-agenda')).toHaveAttribute('aria-checked', 'true')
  await page.keyboard.press('n')
  await expect(title).not.toHaveText(month ?? '')
  await page.keyboard.press('t')
  await expect(title).toHaveText(month ?? '')

  // The browser history follows the URL.
  await page.goBack()
  await expect(title).not.toHaveText(month ?? '')
})

test('opens the read-only details of an event', async ({ page }) => {
  await page.goto('/calendar?view=week')
  await event(page, 'Dentist').click()
  const details = page.getByTestId('event-details')
  await expect(details.getByRole('heading', { name: 'Dentist' })).toBeVisible()
  await expect(details).toContainText('Dr. Weber, Hauptstraße 12')
  await expect(details).toContainText('Mia')
  await expect(page.getByTestId('event-details-level')).toHaveText('You can edit this event')
  await page.keyboard.press('Escape')
  await expect(details).toBeHidden()

  // Keyboard: events are focusable and open with Enter; read-only ones say so.
  const bookClub = event(page, 'Book club')
  await expect(bookClub).toHaveAttribute('aria-label', /read-only/)
  await bookClub.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByTestId('event-details-level')).toHaveText('You can view this event')
})

test('shows busy blocks without a title and without opening them', async ({ page }) => {
  await page.goto('/calendar?view=week')
  const busy = page.getByTestId('busy-event')
  await expect(busy.first()).toBeVisible()
  for (const block of await busy.all()) {
    await expect(block).toHaveText('Busy')
    await expect(block).toHaveAttribute('aria-label', 'Busy')
    await expect(block).not.toHaveAttribute('tabindex', /.*/)
  }
  await busy.first().click({ force: true })
  await expect(page.getByTestId('event-details')).toHaveCount(0)
})

test('toggling a calendar hides its events and survives a reload of the prefs', async ({
  page,
}) => {
  await page.goto('/calendar?view=week')
  await expect(event(page, 'Lunch with Max')).toBeVisible()
  await expect(event(page, 'League match')).toBeVisible()

  await page.getByTestId(`calendar-toggle-${calendars.mia}`).uncheck({ force: true })
  await expect(event(page, 'Lunch with Max')).toHaveCount(0)
  await expect(event(page, 'League match')).toBeVisible()

  // The overlay is stored (mock state lives in the page): another view still hides it.
  await page.getByTestId('calendar-view-agenda').click()
  await expect(page.getByTestId('calendar')).toContainText('League match')
  await expect(page.getByTestId('calendar')).not.toContainText('Lunch with Max')

  await page.getByTestId(`calendar-toggle-${calendars.mia}`).check({ force: true })
  await expect(page.getByTestId('calendar')).toContainText('Lunch with Max')

  // "Shared with me" is a per-device toggle.
  await expect(page.getByTestId('calendar')).toContainText('Parents’ evening')
  await page.getByTestId('calendar-toggle-shared').uncheck({ force: true })
  await expect(page.getByTestId('calendar')).not.toContainText('Parents’ evening')
  await page.reload()
  await expect(page.getByTestId('calendar-toggle-shared')).not.toBeChecked()
})

test('changes the color of a calendar', async ({ page }) => {
  await page.goto('/calendar?view=week')
  await page.getByRole('button', { name: 'Options for FC Lions – Club' }).click()
  await page.getByRole('menuitem', { name: 'Red' }).click()
  await expect(event(page, 'League match')).toHaveCSS('background-color', 'rgb(220, 38, 38)')
})

test.describe('on a phone', () => {
  test.use({ viewport: { width: 360, height: 740 } })

  test('starts with the agenda and fits the width', async ({ page }) => {
    await page.goto('/calendar')
    await expect(page.getByTestId('calendar-view-agenda')).toHaveAttribute('aria-checked', 'true')
    await expect(page.getByTestId('calendar')).toContainText('Lunch with Max')
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    )
    expect(overflow).toBeLessThanOrEqual(0)
  })
})
