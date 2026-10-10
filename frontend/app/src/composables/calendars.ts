import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { CalendarPrefsResponse, CalendarResponse } from '@scalenderplus/api-client'
import { useToast } from '@scalenderplus/ui'
import { api } from '@/api'
import { call } from '@/lib/apiError'
import { fallbackColor } from '@/lib/calendarColors'
import { errorMessage, isApiError } from '@/lib/errorMessages'

export const calendarsQueryKey = ['calendars'] as const
export const calendarPrefsQueryKey = ['calendar-prefs'] as const

/** `GET /calendars`: every calendar the user sees (the sidebar and the views share it). */
export function useCalendars() {
  return useQuery({
    queryKey: calendarsQueryKey,
    queryFn: async () => (await call(api.GET('/api/v1/calendars'))).data.items,
  })
}

/** `GET /me/calendar-prefs`: the personal overlays by calendar id (only stored ones are listed). */
export function useCalendarPrefs() {
  return useQuery({
    queryKey: calendarPrefsQueryKey,
    queryFn: async () => {
      const items = (await call(api.GET('/api/v1/me/calendar-prefs'))).data.items
      return Object.fromEntries(items.map((p) => [p.calendarId, p])) as Record<
        string,
        CalendarPrefsResponse
      >
    },
    staleTime: 60_000,
  })
}

/** A calendar with the user's overlay applied. */
export interface CalendarLayer {
  calendar: CalendarResponse
  color: string
  hidden: boolean
}

/**
 * The calendars of `GET /calendars` with the overlay (prefs of calendars the user no longer sees
 * are ignored). Until the prefs load, every calendar counts as shown.
 */
export function useCalendarLayers() {
  const calendars = useCalendars()
  const prefs = useCalendarPrefs()
  const layers = computed<CalendarLayer[]>(() =>
    (calendars.data.value ?? []).map((calendar) => {
      const p = prefs.data.value?.[calendar.id]
      return {
        calendar,
        color: p?.color ?? calendar.color ?? fallbackColor,
        hidden: p?.hidden ?? false,
      }
    }),
  )
  return { calendars, prefs, layers }
}

/**
 * `PUT /calendars/{id}/prefs` with the overlay's `etag` as `If-Match` (`*` when none is stored yet).
 * The change shows at once; a failure rolls it back. `412`: someone (another tab) changed it — the
 * prefs are reloaded and the user is told.
 */
export function useUpdateCalendarPrefs() {
  const queryClient = useQueryClient()
  const toast = useToast()
  const { t } = useI18n()
  return useMutation({
    mutationFn: async (input: { calendarId: string; hidden: boolean; color: string | null }) => {
      const current =
        queryClient.getQueryData<Record<string, CalendarPrefsResponse>>(calendarPrefsQueryKey)?.[
          input.calendarId
        ]
      const { data } = await call(
        api.PUT('/api/v1/calendars/{id}/prefs', {
          params: {
            path: { id: input.calendarId },
            header: { 'If-Match': current?.etag ?? '*' },
          },
          body: { hidden: input.hidden, color: input.color },
        }),
      )
      return data
    },
    onMutate: async (input) => {
      await queryClient.cancelQueries({ queryKey: calendarPrefsQueryKey })
      const previous =
        queryClient.getQueryData<Record<string, CalendarPrefsResponse>>(calendarPrefsQueryKey)
      queryClient.setQueryData<Record<string, CalendarPrefsResponse>>(
        calendarPrefsQueryKey,
        (old) => ({
          ...old,
          [input.calendarId]: {
            calendarId: input.calendarId,
            hidden: input.hidden,
            color: input.color,
            etag: old?.[input.calendarId]?.etag ?? '*',
          },
        }),
      )
      return { previous }
    },
    onSuccess: (data) => {
      queryClient.setQueryData<Record<string, CalendarPrefsResponse>>(
        calendarPrefsQueryKey,
        (old) => ({ ...old, [data.calendarId]: data }),
      )
    },
    onError: (error, _input, context) => {
      queryClient.setQueryData(calendarPrefsQueryKey, context?.previous)
      if (isApiError(error, 'precondition_failed')) {
        void queryClient.invalidateQueries({ queryKey: calendarPrefsQueryKey })
        toast.show({ variant: 'error', title: t('calendar.prefsChanged') })
      } else {
        toast.show({ variant: 'error', title: errorMessage(t, error) })
      }
    },
  })
}

const sharedWithMeKey = 'scal.calendar.sharedWithMe'

function storedSharedWithMe(): boolean {
  try {
    return localStorage.getItem(sharedWithMeKey) !== 'hidden'
  } catch {
    return true
  }
}

/** Whether "Shared with me" events are shown (a per-device choice, kept in `localStorage`). */
const sharedWithMe = ref(storedSharedWithMe())
watch(sharedWithMe, (shown) => {
  try {
    localStorage.setItem(sharedWithMeKey, shown ? 'shown' : 'hidden')
  } catch {
    // Storage unavailable (private mode): the choice lasts for this page only.
  }
})

export function useSharedWithMe() {
  return sharedWithMe
}
