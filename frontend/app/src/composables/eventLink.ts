import { Temporal } from 'temporal-polyfill'
import { watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { queryOptions, useQueryClient } from '@tanstack/vue-query'
import type { EventResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import { ApiError, call } from '@/lib/apiError'

/**
 * Event links `/e/{id}` and `/e/{id}/edit` (the footer of feed descriptions, ical-caldav.md §1.3):
 * `EventLinkView` loads the event and opens the calendar at its date with `?event={id}` (and
 * `&edit=1`); the calendar then opens the details or the edit dialog from the cached event.
 */
export function eventLinkQuery(id: string) {
  return queryOptions({
    queryKey: ['event-link', id] as const,
    queryFn: async (): Promise<EventResponse> =>
      (await call(api.GET('/api/v1/events/{id}', { params: { path: { id } } }))).data,
    retry: (count, error) => !(error instanceof ApiError) && count < 2,
    staleTime: 30_000,
  })
}

/**
 * The day to open the calendar at: the event's start in the viewer's zone (all-day: its date). A
 * series that started before today opens at today — its first occurrence is rarely what a link
 * from a calendar app is about.
 */
export function eventLinkDate(event: EventResponse, timeZone: string): Temporal.PlainDate {
  const zone = knownZone(timeZone)
  const todayDate = Temporal.Now.plainDateISO(zone)
  const start = event.start.date
    ? Temporal.PlainDate.from(event.start.date)
    : event.start.utc
      ? Temporal.Instant.from(event.start.utc).toZonedDateTimeISO(zone).toPlainDate()
      : todayDate
  if (event.recurrence && Temporal.PlainDate.compare(start, todayDate) < 0) return todayDate
  return start
}

function knownZone(timeZone: string): string {
  try {
    Temporal.Now.plainDateISO(timeZone)
    return timeZone
  } catch {
    return 'UTC'
  }
}

/** Busy blocks have nothing to open (title and details are hidden). */
export const isBusyBlock = (event: EventResponse) =>
  event.myLevel === 'free_busy' || event.myLevel === 'none'

/**
 * Calendar side of event links: on `?event={id}[&edit=1]` removes the parameters from the URL
 * (closing the dialog must not reopen it) and calls `open` with the event once it has loaded.
 */
export function useLinkedEvent(open: (event: EventResponse, edit: boolean) => void) {
  const route = useRoute()
  const router = useRouter()
  const queryClient = useQueryClient()

  watch(
    () => route.query.event,
    async (id) => {
      if (typeof id !== 'string' || id === '') return
      const edit = route.query.edit === '1'
      const query = { ...route.query }
      delete query.event
      delete query.edit
      void router.replace({ query })
      try {
        const event = await queryClient.fetchQuery(eventLinkQuery(id))
        if (!isBusyBlock(event)) open(event, edit)
      } catch {
        // Gone in the meantime: the calendar at that date is all there is to show.
      }
    },
    { immediate: true },
  )
}
