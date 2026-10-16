import { describe, expect, it } from 'vitest'
import type { EventResponse } from '@scalenderplus/api-client'
import { mockEvent, mockResolveWallTime, mockSeries } from '@scalenderplus/api-client/mocks'
import {
  droppedTimes,
  endBeforeStart,
  eventValues,
  isRuleOccurrence,
  newEventValues,
  shiftSeriesTimes,
  toCreateRequest,
  toMergePatch,
  toOccurrencePatch,
} from '@/lib/eventForm'
import { describeRule } from '@/lib/recurrenceText'
import { createAppI18n } from '@/i18n'

const t = createAppI18n('en').global.t as (key: string, named?: Record<string, unknown>) => string
const calendarId = mockEvent.calendarId

describe('event form → payload', () => {
  it('maps a selected slot to a timed event in the user zone', () => {
    const v = newEventValues({
      calendarId,
      timeZone: 'Europe/Berlin',
      start: '2026-11-02T09:00:00',
      end: '2026-11-02T10:30:00',
      allDay: false,
    })
    expect(toCreateRequest({ ...v, title: '  Standup ' })).toEqual({
      calendarId,
      title: 'Standup',
      start: { dateTime: '2026-11-02T09:00:00', timeZone: 'Europe/Berlin' },
      end: { dateTime: '2026-11-02T10:30:00', timeZone: 'Europe/Berlin' },
    })
  })

  it('defaults to one hour without an end and keeps a chosen zone', () => {
    const v = newEventValues({
      calendarId,
      timeZone: 'Europe/Berlin',
      start: '2026-11-02T23:30',
      allDay: false,
    })
    expect(v).toMatchObject({ endDate: '2026-11-03', endTime: '00:30' })
    expect(toCreateRequest({ ...v, title: 'Call', timeZone: 'America/New_York' }).start).toEqual({
      dateTime: '2026-11-02T23:30:00',
      timeZone: 'America/New_York',
    })
  })

  it('shows the last day of all-day events and sends an exclusive end', () => {
    const v = newEventValues({
      calendarId,
      timeZone: 'UTC',
      start: '2026-11-02',
      end: '2026-11-05',
      allDay: true,
    })
    expect(v).toMatchObject({ startDate: '2026-11-02', endDate: '2026-11-04', allDay: true })
    const body = toCreateRequest({ ...v, title: 'Camp', location: ' Lake ', description: '' })
    expect(body).toEqual({
      calendarId,
      title: 'Camp',
      start: { date: '2026-11-02' },
      end: { date: '2026-11-05' },
      location: 'Lake',
    })
    const event = { ...mockEvent, allDay: true, start: body.start!, end: body.end! }
    expect(eventValues(event as EventResponse, 'UTC')).toMatchObject({
      startDate: '2026-11-02',
      endDate: '2026-11-04',
    })
  })

  it('validates that the end is after the start', () => {
    const v = eventValues(mockEvent, 'UTC')
    expect(endBeforeStart(v)).toBe(false)
    expect(endBeforeStart({ ...v, endTime: '18:00' })).toBe(true)
    expect(endBeforeStart({ ...v, allDay: true, endDate: v.startDate })).toBe(false)
    expect(endBeforeStart({ ...v, allDay: true, endDate: '2026-11-01' })).toBe(true)
  })

  it('reads an event in its own zone, not the viewer’s', () => {
    expect(eventValues(mockEvent, 'Asia/Tokyo')).toMatchObject({
      startDate: '2026-11-02',
      startTime: '18:00',
      endTime: '20:00',
      timeZone: 'Europe/Berlin',
    })
  })

  it('merge-patches only changed members, removing emptied texts and pairing times', () => {
    const before = eventValues(mockEvent, 'UTC')
    expect(toMergePatch(before, { ...before })).toEqual({})
    expect(toMergePatch(before, { ...before, title: 'Match', location: '' })).toEqual({
      title: 'Match',
      location: '',
    })
    expect(toMergePatch(before, { ...before, endTime: '21:00' })).toEqual({
      start: { dateTime: '2026-11-02T18:00:00', timeZone: 'Europe/Berlin' },
      end: { dateTime: '2026-11-02T21:00:00', timeZone: 'Europe/Berlin' },
    })
    expect(toMergePatch(before, { ...before, allDay: true })).toEqual({
      start: { date: '2026-11-02' },
      end: { date: '2026-11-03' },
    })
    // Hidden time fields of an all-day event do not count as changes.
    const allDay = { ...before, allDay: true }
    expect(toMergePatch(allDay, { ...allDay, startTime: '07:00' })).toEqual({})
  })

  it('sends a cover title on create only when set and removes a cleared one with null', () => {
    const v = newEventValues({
      calendarId,
      timeZone: 'UTC',
      start: '2026-11-02T09:00:00',
      allDay: false,
    })
    expect(toCreateRequest({ ...v, title: 'Therapy', coverTitle: '  ' })).not.toHaveProperty(
      'coverTitle',
    )
    expect(toCreateRequest({ ...v, title: 'Therapy', coverTitle: ' Appointment ' })).toMatchObject({
      coverTitle: 'Appointment',
    })

    const before = eventValues({ ...mockEvent, coverTitle: 'Sport' }, 'UTC')
    expect(before.coverTitle).toBe('Sport')
    expect(toMergePatch(before, { ...before, coverTitle: ' Sport ' })).toEqual({})
    expect(toMergePatch(before, { ...before, coverTitle: 'Gym' })).toEqual({ coverTitle: 'Gym' })
    expect(toMergePatch(before, { ...before, coverTitle: ' ' })).toEqual({ coverTitle: null })
    expect(eventValues(mockEvent, 'UTC').coverTitle).toBe('')
  })

  it('patches occurrences with their members only', () => {
    const before = eventValues(mockSeries, 'UTC')
    expect(toOccurrencePatch(before, { ...before, title: 'Moved', startTime: '19:00' })).toEqual({
      title: 'Moved',
      start: { dateTime: '2026-11-03T19:00:00', timeZone: 'Europe/Berlin' },
      end: { dateTime: '2026-11-03T19:30:00', timeZone: 'Europe/Berlin' },
    })
    expect(toOccurrencePatch(before, { ...before, coverTitle: 'Busy evening' })).toEqual({
      coverTitle: 'Busy evening',
    })
  })

  it('moves a series by the change of one occurrence', () => {
    const occurrence = {
      ...mockSeries,
      start: { ...mockSeries.start, dateTime: '2026-11-17T18:00:00' },
      end: { ...mockSeries.end, dateTime: '2026-11-17T19:30:00' },
    }
    const before = eventValues(occurrence, 'UTC')
    const after = { ...before, startTime: '19:00', endTime: '21:00' }
    expect(shiftSeriesTimes(mockSeries, before, after)).toEqual({
      start: { dateTime: '2026-11-03T19:00:00', timeZone: 'Europe/Berlin' },
      end: { dateTime: '2026-11-03T21:00:00', timeZone: 'Europe/Berlin' },
    })
  })

  it('converts dropped times from the view zone to the event zone', () => {
    expect(
      droppedTimes({
        start: '2026-11-02T12:00:00',
        end: '2026-11-02T13:00:00',
        allDay: false,
        viewZone: 'America/New_York',
        eventZone: 'Europe/Berlin',
      }),
    ).toEqual({
      start: { dateTime: '2026-11-02T18:00:00', timeZone: 'Europe/Berlin' },
      end: { dateTime: '2026-11-02T19:00:00', timeZone: 'Europe/Berlin' },
    })
    expect(
      droppedTimes({
        start: '2026-11-02',
        end: '2026-11-04',
        allDay: true,
        viewZone: 'UTC',
        eventZone: 'UTC',
      }),
    ).toEqual({ start: { date: '2026-11-02' }, end: { date: '2026-11-04' } })
  })

  it('offers "this and following" on rule occurrences only', () => {
    const occurrence = {
      ...mockSeries,
      recurrenceId: '2026-11-10T17:00:00Z',
      start: { ...mockSeries.start, utc: '2026-11-10T17:00:00Z' },
    }
    expect(isRuleOccurrence(occurrence)).toBe(true)
    expect(isRuleOccurrence(mockEvent)).toBe(false)
    // An extra date (rdate) is not an occurrence of the rule.
    expect(
      isRuleOccurrence({
        ...occurrence,
        recurrence: { rrule: 'FREQ=WEEKLY', rdates: ['2026-11-10T18:00:00'] },
      }),
    ).toBe(false)
    // Shifted by a DST gap: the start differs from the recurrence id without being modified.
    expect(
      isRuleOccurrence({
        ...occurrence,
        start: { ...occurrence.start, utc: '2026-11-10T18:00:00Z' },
      }),
    ).toBe(false)
    expect(
      isRuleOccurrence({
        ...occurrence,
        modified: true,
        start: { ...occurrence.start, utc: '2026-11-10T18:00:00Z' },
      }),
    ).toBe(true)
  })

  it('describes a rule read-only', () => {
    expect(describeRule(t, 'FREQ=WEEKLY;COUNT=8')).toBe('Weekly, 8 times')
    expect(describeRule(t, 'RRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20261231')).toBe(
      'Every 2 days, until 2026-12-31',
    )
    expect(describeRule(t, 'FREQ=HOURLY')).toBe('FREQ=HOURLY')
  })

  it('mocks resolve DST gaps forward like the api', () => {
    expect(mockResolveWallTime('2027-03-28T02:30:00', 'Europe/Berlin')).toEqual({
      utc: '2027-03-28T01:30:00Z',
      dateTime: '2027-03-28T03:30:00',
      shifted: true,
    })
    expect(mockResolveWallTime('2026-11-02T18:00:00', 'Europe/Berlin')).toEqual({
      utc: '2026-11-02T17:00:00Z',
      dateTime: '2026-11-02T18:00:00',
      shifted: false,
    })
  })
})
