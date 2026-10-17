/// <reference types="node" />
/**
 * Cross-parser validation of the iCal feeds (docs/architecture/ical-caldav.md §1.5): the backend's
 * golden feed files are parsed with ical.js — a second iCalendar implementation next to Ical.Net in
 * the backend suite — and every time with a TZID is resolved through the file's own VTIMEZONE
 * (ical.js ships no zone database), so the instants must equal those the backend computed with its
 * bundled tzdb (the `*.instants.json` sidecars). CI fails on unparsable or inconsistent output.
 */
import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import ICAL from 'ical.js'
import { afterEach, describe, expect, it } from 'vitest'

const goldenDir = join(
  dirname(fileURLToPath(import.meta.url)),
  '../../../../../backend/tests/SCalenderPlus.Application.Tests/Feeds/Golden',
)
const files = readdirSync(goldenDir)
  .filter((f) => f.endsWith('.ics'))
  .sort()

interface Instants {
  events: { uid: string; recurrenceId: string | null; start: string; end: string }[]
  occurrences: Record<string, string[]>
}

function load(file: string): { text: string; calendar: ICAL.Component; instants: Instants } {
  const text = readFileSync(join(goldenDir, file), 'utf8')
  const instants = JSON.parse(
    readFileSync(join(goldenDir, file.replace(/\.ics$/, '.instants.json')), 'utf8'),
  ) as Instants
  const calendar = new ICAL.Component(ICAL.parse(text))
  // Only the file's own VTIMEZONEs: what a client without a zone database would use.
  for (const vtimezone of calendar.getAllSubcomponents('vtimezone')) {
    ICAL.TimezoneService.register(vtimezone)
  }
  return { text, calendar, instants }
}

const iso = (time: ICAL.Time) =>
  new Date(time.toUnixTime() * 1000).toISOString().replace('.000Z', 'Z')

afterEach(() => ICAL.TimezoneService.reset())

it('finds the golden files', () => {
  expect(files.length).toBeGreaterThanOrEqual(10)
})

describe.each(files)('%s', (file) => {
  it('parses with ical.js with every VEVENT intact', () => {
    const { text, calendar } = load(file)

    expect(calendar.name).toBe('vcalendar')
    expect(calendar.getFirstPropertyValue('version')).toBe('2.0')
    expect(calendar.getFirstPropertyValue('prodid')).toBe('-//sCalenderPlus//EN')
    const vevents = calendar.getAllSubcomponents('vevent')
    expect(vevents).toHaveLength(text.match(/^BEGIN:VEVENT\r$/gm)?.length ?? -1)
    for (const vevent of vevents) {
      const event = new ICAL.Event(vevent)
      expect(event.uid).toMatch(/\S/)
      expect(event.summary).toMatch(/\S/)
      expect(event.endDate.compare(event.startDate)).toBeGreaterThanOrEqual(0)
      expect(vevent.getFirstProperty('attendee')).toBeNull()
      expect(vevent.getFirstProperty('organizer')).toBeNull()
      expect(vevent.getAllSubcomponents('valarm')).toHaveLength(0)
    }
    // Lines are CRLF-terminated and at most 75 octets (RFC 5545 §3.1).
    for (const line of text.split('\r\n')) {
      expect(new TextEncoder().encode(line).length).toBeLessThanOrEqual(75)
    }
  })

  it('resolves every TZID through its VTIMEZONE to the instants of the bundled tzdb', () => {
    const { calendar, instants } = load(file)

    for (const vevent of calendar.getAllSubcomponents('vevent')) {
      const event = new ICAL.Event(vevent)
      if (event.startDate.isDate) continue // all-day: floating dates
      const zone = event.startDate.zone?.tzid ?? 'floating'
      expect(ICAL.TimezoneService.has(zone), `VTIMEZONE for ${zone}`).toBe(true)
      const recurrenceId = event.recurrenceId ? iso(event.recurrenceId) : null
      const expected = instants.events.find(
        (e) => e.uid === event.uid && e.recurrenceId === recurrenceId,
      )
      expect(expected, `${event.uid} ${recurrenceId}`).toBeDefined()
      expect(iso(event.startDate)).toBe(expected!.start)
      expect(iso(event.endDate)).toBe(expected!.end)
    }
  })

  it('expands series in the same local time across DST', () => {
    const { calendar, instants } = load(file)

    for (const vevent of calendar.getAllSubcomponents('vevent')) {
      const event = new ICAL.Event(vevent)
      if (!event.isRecurring() || event.startDate.isDate) continue
      const excluded = new Set(
        vevent
          .getAllProperties('exdate')
          .flatMap((p) => p.getValues() as ICAL.Time[])
          .map((t) => iso(t)),
      )
      const expected = (instants.occurrences[event.uid] ?? []).filter((t) => !excluded.has(t))
      const iterator = event.iterator()
      const actual: string[] = []
      for (
        let next = iterator.next();
        next && actual.length < expected.length;
        next = iterator.next()
      ) {
        actual.push(iso(next))
      }
      expect(expected.length, event.uid).toBeGreaterThan(0)
      expect(actual).toEqual(expected)
    }
  })
})
