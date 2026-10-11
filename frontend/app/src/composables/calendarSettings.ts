import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { useI18n } from 'vue-i18n'
import { queryOptions, useQuery, useQueryClient, type QueryClient } from '@tanstack/vue-query'
import type { CalendarResponse, GrantResponse } from '@scalenderplus/api-client'
import { useToast } from '@scalenderplus/ui'
import { api } from '@/api'
import { ApiError, call } from '@/lib/apiError'
import { isApiError } from '@/lib/errorMessages'
import { calendarsQueryKey } from './calendars'

export const calendarKeys = {
  detail: (id: string) => ['calendars', id] as const,
  grants: (id: string) => ['calendars', id, 'grants'] as const,
}

/** A calendar with the strong `ETag` of `GET /calendars/{id}` (the `If-Match` of PATCH/DELETE). */
export interface CalendarWithEtag {
  calendar: CalendarResponse
  etag: string | null
}

export function calendarQuery(id: string) {
  return queryOptions({
    queryKey: calendarKeys.detail(id),
    queryFn: async (): Promise<CalendarWithEtag> => {
      const { data, response } = await call(
        api.GET('/api/v1/calendars/{id}', { params: { path: { id } } }),
      )
      return { calendar: data, etag: response.headers.get('ETag') }
    },
    retry: (count, error) => !(error instanceof ApiError) && count < 2,
  })
}

export function grantsQuery(id: string) {
  return queryOptions({
    queryKey: calendarKeys.grants(id),
    queryFn: async (): Promise<GrantResponse[]> => {
      const items: GrantResponse[] = []
      let cursor: string | undefined
      do {
        const { data } = await call(
          api.GET('/api/v1/calendars/{id}/grants', {
            params: { path: { id }, query: { limit: 200, cursor } },
          }),
        )
        items.push(...data.items)
        cursor = data.nextCursor ?? undefined
      } while (cursor)
      return items
    },
    retry: (count, error) => !(error instanceof ApiError) && count < 2,
    staleTime: 0,
  })
}

export function useCalendar(id: MaybeRefOrGetter<string>) {
  return useQuery(computed(() => calendarQuery(toValue(id))))
}

export function useGrants(id: MaybeRefOrGetter<string>, enabled: MaybeRefOrGetter<boolean>) {
  return useQuery(computed(() => ({ ...grantsQuery(toValue(id)), enabled: toValue(enabled) })))
}

/** After a change: reload the calendar (new `ETag`), its grants and the calendar list (`myLevel`). */
export async function refreshCalendar(queryClient: QueryClient, id: string) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: calendarKeys.detail(id), exact: true }),
    queryClient.invalidateQueries({ queryKey: calendarKeys.grants(id) }),
    queryClient.invalidateQueries({ queryKey: calendarsQueryKey, exact: true }),
  ])
}

/**
 * Runs a change guarded by `If-Match`. A `412` (changed elsewhere) reloads the calendar and its
 * grants and tells the user; the caller keeps the unsaved edits so they can try again.
 */
export function useCalendarChange(id: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  const toast = useToast()
  const { t } = useI18n()
  return async function run<T>(change: () => Promise<T>): Promise<T> {
    try {
      const result = await change()
      await refreshCalendar(queryClient, toValue(id))
      return result
    } catch (error) {
      if (isApiError(error, 'precondition_failed', 'precondition_required')) {
        await refreshCalendar(queryClient, toValue(id))
        toast.show({ variant: 'error', title: t('calendarSettings.errors.changed') })
      }
      throw error
    }
  }
}
