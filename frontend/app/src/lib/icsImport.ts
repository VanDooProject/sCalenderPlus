import type { IcsUploadItem, IcsUploadResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import { call } from './apiError'

/** Largest file the api takes (ical-caldav.md §2); checked before uploading. */
export const maxIcsBytes = 5 * 1024 * 1024

/** Codes of the api's report that have a message (`icsImport.reasons.*`, `icsImport.warnings.*`). */
export const icsReasons = [
  'exists',
  'duplicate_in_file',
  'start_missing',
  'time_invalid',
  'recurrence_not_supported',
  'recurrence_invalid',
  'cancelled',
] as const

export const icsWarnings = [
  'time_zone_unknown',
  'floating_time',
  'title_missing',
  'truncated',
  'url_invalid',
  'private_event',
  'end_before_start',
  'time_adjusted_dst',
  'uid_generated',
  'dates_capped',
  'rdate_period_ignored',
  'occurrence_not_found',
  'occurrence_ignored',
  'orphan_occurrence',
  'unsupported_components',
] as const

export type IcsAction = 'create' | 'duplicate' | 'skip'

/** Uploads the file's text: a preview (`dryRun`) or the import. */
export async function uploadIcs(
  calendarId: string,
  text: string,
  dryRun: boolean,
): Promise<IcsUploadResponse> {
  const { data } = await call(
    api.POST('/api/v1/calendars/{id}/ics-upload', {
      params: { path: { id: calendarId }, query: { dryRun } },
      body: text,
      bodySerializer: (body: string) => body,
      headers: { 'Content-Type': 'text/calendar; charset=utf-8' },
    }),
  )
  return data
}

/** A message key of a reason or warning code, or null for codes this app version does not know. */
export function reasonKey(code: string | null | undefined): string | null {
  return code && (icsReasons as readonly string[]).includes(code)
    ? `icsImport.reasons.${code}`
    : null
}

export function warningKey(code: string): string | null {
  return (icsWarnings as readonly string[]).includes(code) ? `icsImport.warnings.${code}` : null
}

/**
 * When an item starts, as the file says it (wall clock of its zone, or the date): uploads show the
 * file's own times, not converted ones.
 */
export function formatItemStart(item: IcsUploadItem, locale: string): string {
  const start = item.start
  if (!start) return ''
  if (start.date) {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' }).format(
      new Date(`${start.date}T00:00:00Z`),
    )
  }
  if (!start.dateTime) return ''
  const text = new Intl.DateTimeFormat(locale, {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'UTC',
  }).format(new Date(`${start.dateTime}Z`))
  return start.timeZone ? `${text} (${start.timeZone.replaceAll('_', ' ')})` : text
}
