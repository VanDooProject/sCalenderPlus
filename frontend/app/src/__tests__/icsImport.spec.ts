import { afterEach, describe, expect, it } from 'vitest'
import {
  mockCalendar,
  mockFrozenCalendar,
  mockReadOnlyCalendar,
} from '@scalenderplus/api-client/mocks'
import { byTestId, click, mountApp, queryTestId, resetDom, settle, until } from './app'
import { useMockApi } from './msw'

const server = useMockApi()

afterEach(() => {
  server.events.removeAllListeners()
  resetDom()
})

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? ''

/** A small export: a weekly series with a moved and a cancelled occurrence, an all-day event, a private one. */
const exportFile = [
  'BEGIN:VCALENDAR',
  'VERSION:2.0',
  'BEGIN:VEVENT',
  'UID:training@example.test',
  'SUMMARY:U12 training',
  'DTSTART;TZID=Europe/Berlin:20261102T180000',
  'DTEND;TZID=Europe/Berlin:20261102T193000',
  'RRULE:FREQ=WEEKLY;COUNT=10',
  'EXDATE;TZID=Europe/Berlin:20261109T180000',
  'END:VEVENT',
  'BEGIN:VEVENT',
  'UID:training@example.test',
  'RECURRENCE-ID;TZID=Europe/Berlin:20261116T180000',
  'SUMMARY:U12 training (hall)',
  'DTSTART;TZID=Europe/Berlin:20261116T190000',
  'END:VEVENT',
  'BEGIN:VEVENT',
  'UID:tournament@example.test',
  'SUMMARY:Tournament',
  'DTSTART;VALUE=DATE:20261121',
  'END:VEVENT',
  'BEGIN:VEVENT',
  'UID:board@example.test',
  'SUMMARY:Board',
  'CLASS:PRIVATE',
  'DTSTART:20261105T190000Z',
  'END:VEVENT',
  'END:VCALENDAR',
  '',
].join('\r\n')

/** Puts a file into the dialog's file input (jsdom cannot open a file chooser). */
async function pick(content: string, name = 'export.ics', size?: number) {
  const input = byTestId('ics-import-input') as HTMLInputElement
  const file = new File([content], name, { type: 'text/calendar' })
  if (size !== undefined) Object.defineProperty(file, 'size', { value: size })
  Object.defineProperty(input, 'files', { value: [file], configurable: true })
  input.dispatchEvent(new Event('change'))
  await settle()
}

async function openDialog(id = mockCalendar.id) {
  await mountApp(`/calendars/${id}/settings`)
  await until(() => queryTestId('calendar-settings-title') !== null)
  await click('ics-import-open')
  await until(() => queryTestId('ics-import-dialog') !== null)
}

function uploads() {
  const requests: { dryRun: string | null; contentType: string | null; body: string }[] = []
  server.events.on('request:start', async ({ request }) => {
    const url = new URL(request.url)
    if (!url.pathname.endsWith('/ics-upload')) return
    requests.push({
      dryRun: url.searchParams.get('dryRun'),
      contentType: request.headers.get('Content-Type'),
      body: await request.clone().text(),
    })
  })
  return requests
}

describe('ICS import', () => {
  it('previews a file, then imports it; the same file again has nothing new', async () => {
    const requests = uploads()
    await openDialog()

    await pick(exportFile)
    await until(() => queryTestId('ics-import-counts') !== null)

    expect(requests[0]).toMatchObject({ dryRun: 'true', body: exportFile })
    expect(requests[0]!.contentType).toMatch(/^text\/calendar/)
    expect(text(byTestId('ics-import-file'))).toBe('export.ics')
    expect(text(byTestId('ics-import-created'))).toBe('3 new events')
    expect(text(byTestId('ics-import-counts'))).toContain('None already here')
    expect(text(byTestId('ics-warning-private_event'))).toContain('marked private')
    const items = document.querySelectorAll('[data-testid="ics-import-item"]')
    expect(items).toHaveLength(3)
    expect(text(items[0]!)).toContain('U12 training')
    expect(text(items[0]!)).toContain('Recurring · 1 changed date · 1 cancelled date')
    expect(text(items[0]!)).toContain('New')

    await click('ics-import-submit')
    await until(() => queryTestId('ics-import-dialog') === null)
    expect(requests[1]).toMatchObject({ dryRun: 'false', body: exportFile })
    expect(text(document.body)).toContain('3 events imported')

    await click('ics-import-open')
    await until(() => queryTestId('ics-import-dialog') !== null)
    await pick(exportFile)
    await until(() => queryTestId('ics-import-counts') !== null)
    expect(text(byTestId('ics-import-duplicates'))).toBe('3 already here')
    expect(queryTestId('ics-import-nothing')).not.toBeNull()
    expect((byTestId('ics-import-submit') as HTMLButtonElement).disabled).toBe(true)
    expect(text(document.querySelector('[data-testid="ics-import-item"]'))).toContain(
      'already in this calendar',
    )
  })

  it('refuses files that are too large before uploading, and shows invalid files', async () => {
    const requests = uploads()
    await openDialog()

    await pick(exportFile, 'huge.ics', 6 * 1024 * 1024)
    expect(text(byTestId('ics-import-dialog'))).toContain('larger than 5 MB')
    expect(requests).toHaveLength(0)

    await pick('<html>no calendar</html>', 'page.html')
    await until(() => text(byTestId('ics-import-dialog')).includes('not a valid calendar'))
    expect(queryTestId('ics-import-counts')).toBeNull()
  })

  it('is offered from contribute on and paused in frozen calendars', async () => {
    await mountApp(`/calendars/${mockReadOnlyCalendar.id}/settings`)
    await until(() => queryTestId('calendar-settings-title') !== null)
    expect(queryTestId('ics-import-open')).toBeNull() // read only

    resetDom()
    await mountApp(`/calendars/${mockFrozenCalendar.id}/settings`)
    await until(() => queryTestId('calendar-settings-title') !== null)
    expect((byTestId('ics-import-open') as HTMLButtonElement).disabled).toBe(true)
    expect(queryTestId('ics-import-frozen')).not.toBeNull()
  })

  it('speaks German', async () => {
    await mountApp(`/calendars/${mockCalendar.id}/settings`, { locale: 'de' })
    await until(() => queryTestId('calendar-settings-title') !== null)
    await click('ics-import-open')
    await until(() => queryTestId('ics-import-dialog') !== null)
    await pick(exportFile)
    await until(() => queryTestId('ics-import-counts') !== null)
    expect(text(byTestId('ics-import-created'))).toBe('3 neue Termine')
    expect(text(byTestId('ics-import-submit'))).toBe('3 Termine importieren')
  })
})
