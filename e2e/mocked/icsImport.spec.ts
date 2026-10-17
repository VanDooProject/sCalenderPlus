import { expect, test, type Page } from '@playwright/test'
import { expectAccessible } from '../support/a11y'
import { calendars } from './fixtures'

/**
 * ICS upload from the calendar settings against the MSW import mock (frontend/packages/api-client/
 * src/mocks/imports.ts): preview with counts and warnings, import, and a second upload of the same
 * file that has nothing new. Mock state lives in the page: a reload resets it.
 */

const googleExport = [
  'BEGIN:VCALENDAR',
  'PRODID:-//Google Inc//Google Calendar 70.9054//EN',
  'VERSION:2.0',
  'X-WR-TIMEZONE:Europe/Berlin',
  'BEGIN:VEVENT',
  'DTSTART;TZID=Europe/Berlin:20261102T180000',
  'DTEND;TZID=Europe/Berlin:20261102T193000',
  'RRULE:FREQ=WEEKLY;UNTIL=20261221T225959Z;BYDAY=MO',
  'EXDATE;TZID=Europe/Berlin:20261109T180000',
  'UID:training@google.com',
  'SUMMARY:U12 training',
  'END:VEVENT',
  'BEGIN:VEVENT',
  'DTSTART;TZID=Europe/Berlin:20261116T190000',
  'DTEND;TZID=Europe/Berlin:20261116T203000',
  'RECURRENCE-ID;TZID=Europe/Berlin:20261116T180000',
  'UID:training@google.com',
  'SUMMARY:U12 training (hall)',
  'END:VEVENT',
  'BEGIN:VEVENT',
  'DTSTART;VALUE=DATE:20261121',
  'DTEND;VALUE=DATE:20261122',
  'UID:tournament@google.com',
  'SUMMARY:Tournament',
  'END:VEVENT',
  'BEGIN:VEVENT',
  'DTSTART:20261105T190000Z',
  'DTEND:20261105T200000Z',
  'UID:board@google.com',
  'CLASS:PRIVATE',
  'SUMMARY:Board',
  'END:VEVENT',
  'BEGIN:VEVENT',
  'DTSTART;TZID=Europe/Berlin:20261120T180000',
  'UID:cancelled@google.com',
  'STATUS:CANCELLED',
  'SUMMARY:Friendly (cancelled)',
  'END:VEVENT',
  'END:VCALENDAR',
  '',
].join('\r\n')

async function upload(page: Page, content = googleExport, name = 'lions.ics') {
  await page.getByTestId('ics-import-input').setInputFiles({
    name,
    mimeType: 'text/calendar',
    buffer: Buffer.from(content),
  })
}

test('import a Google Calendar export from the calendar settings', async ({ page }) => {
  const uploads: string[] = []
  page.on('request', (r) => {
    const url = new URL(r.url())
    if (url.pathname.endsWith('/ics-upload')) uploads.push(`${r.method()} ${url.search}`)
  })
  await page.goto(`/calendars/${calendars.lions}/settings`)
  await page.getByRole('button', { name: 'Import .ics file' }).click()
  const dialog = page.getByRole('dialog', {
    name: 'Import into FC Lions – Club',
  })

  await upload(page)
  await expect(dialog.getByTestId('ics-import-created')).toHaveText('3 new events')
  await expect(dialog.getByTestId('ics-import-skipped')).toHaveText("1 can't be imported")
  await expect(dialog.getByTestId('ics-warning-private_event')).toContainText('marked private')
  await expect(dialog.getByTestId('ics-import-item').first()).toContainText(
    'Recurring · 1 changed date · 1 cancelled date',
  )
  await expect(dialog.getByTestId('ics-import-item').last()).toContainText('Skipped')
  await expectAccessible(page, 'ics import preview')
  await dialog.getByRole('button', { name: 'Import 3 events' }).click()

  await expect(page.getByTestId('toast')).toContainText('3 events imported')
  await expect(dialog).toBeHidden()
  expect(uploads).toEqual(['POST ?dryRun=true', 'POST ?dryRun=false'])

  // The same file again: nothing new.
  await page.getByRole('button', { name: 'Import .ics file' }).click()
  await upload(page)
  await expect(dialog.getByTestId('ics-import-duplicates')).toHaveText('3 already here')
  await expect(dialog.getByTestId('ics-import-nothing')).toBeVisible()
  await expect(dialog.getByTestId('ics-import-submit')).toBeDisabled()
})

test('an invalid file is explained', async ({ page }) => {
  await page.goto(`/calendars/${calendars.mia}/settings`)
  await page.getByRole('button', { name: 'Import .ics file' }).click()
  await upload(page, '<html>not a calendar</html>', 'page.html')
  await expect(page.getByTestId('ics-import-dialog')).toContainText(
    'The file is not a valid calendar (.ics) file.',
  )
})

test('frozen calendars pause importing; readers get no import', async ({ page }) => {
  await page.goto(`/calendars/${calendars.frozen}/settings`)
  await expect(page.getByRole('button', { name: 'Import .ics file' })).toBeDisabled()
  await page.goto(`/calendars/${calendars.bookClub}/settings`)
  await expect(page.getByTestId('calendar-settings-title')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Import .ics file' })).toHaveCount(0)
})

test.describe('with a German browser', () => {
  test.use({ locale: 'de-DE' })

  test('German import dialog', async ({ page }) => {
    await page.goto(`/calendars/${calendars.lions}/settings`)
    await page.getByRole('button', { name: '.ics-Datei importieren' }).click()
    await upload(page)
    await expect(page.getByTestId('ics-import-created')).toHaveText('3 neue Termine')
    await expect(page.getByRole('button', { name: '3 Termine importieren' })).toBeEnabled()
  })
})
