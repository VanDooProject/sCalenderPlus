<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import FullCalendar from '@fullcalendar/vue3'
import type {
  CalendarOptions,
  EventClickInfo,
  EventDisplayInfo,
  MountInfo,
} from '@fullcalendar/vue3'
import classicTheme from '@fullcalendar/vue3/themes/classic'
import dayGridPlugin from '@fullcalendar/vue3/daygrid'
import timeGridPlugin from '@fullcalendar/vue3/timegrid'
import listPlugin from '@fullcalendar/vue3/list'
import interactionPlugin from '@fullcalendar/vue3/interaction'
import deLocale from '@fullcalendar/vue3/locales/de'
import '@fullcalendar/vue3/skeleton.css'
import '@fullcalendar/vue3/themes/classic/theme.css'
import './calendar.css'
import { CalendarDays, ChevronLeft, ChevronRight, Lock } from '@lucide/vue'
import type { EventResponse } from '@scalenderplus/api-client'
import { UiAlert, UiButton, UiSpinner } from '@scalenderplus/ui'
import EmptyState from '@/components/EmptyState.vue'
import EventDetailsDialog from '@/components/calendar/EventDetailsDialog.vue'
import { useCalendarLayers, useSharedWithMe } from '@/composables/calendars'
import { useSession } from '@/composables/session'
import { api } from '@/api'
import { call } from '@/lib/apiError'
import { fallbackColor, sharedColor } from '@/lib/calendarColors'
import { errorMessage } from '@/lib/errorMessages'
import {
  calendarViews,
  eventsQueryKey,
  eventsQueryParams,
  firstDayOf,
  fullCalendarViews,
  parseDate,
  parseView,
  shiftDate,
  toCalendarEvent,
  today,
  visibleEvents,
  visibleRange,
  type CalendarViewName,
  type EventsWindow,
  type MappedEvent,
} from '@/lib/calendarView'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const { user } = useSession()
const { calendars, prefs, layers } = useCalendarLayers()
const sharedWithMe = useSharedWithMe()

const timeZone = computed(() => user.value?.timeZone ?? 'UTC')
const firstDay = computed(() => firstDayOf(user.value?.weekStart))

/** Small screens start in the agenda (a grid of seven columns is unreadable at 360 px). */
const compact = window.matchMedia?.('(max-width: 639px)').matches ?? false
const view = computed<CalendarViewName>(
  () => parseView(route.query.view) ?? (compact ? 'agenda' : 'week'),
)
const date = computed(() => parseDate(route.query.date) ?? today(timeZone.value))
const isToday = computed(() => date.value.equals(today(timeZone.value)))

function go(next: { view?: CalendarViewName; date?: string }) {
  void router.push({
    query: {
      ...route.query,
      view: next.view ?? view.value,
      date: next.date ?? date.value.toString(),
    },
  })
}
const goToday = () => go({ date: today(timeZone.value).toString() })
const step = (direction: 1 | -1) =>
  go({ date: shiftDate(view.value, date.value, direction).toString() })

/** Shown calendars: those of `GET /calendars` not hidden by the user's overlay. */
const shownIds = computed(() => layers.value.filter((l) => !l.hidden).map((l) => l.calendar.id))
const colorOf = computed(() => {
  const colors = new Map(layers.value.map((l) => [l.calendar.id, l.color]))
  return (id: string) => colors.get(id) ?? fallbackColor
})

const eventsWindow = computed<EventsWindow>(() => ({
  ...visibleRange(view.value, date.value, firstDay.value, timeZone.value),
  timeZone: timeZone.value,
  calendarIds: shownIds.value,
  sharedWithMe: sharedWithMe.value,
}))
const hasSources = computed(() => shownIds.value.length > 0 || sharedWithMe.value)

const events = useQuery({
  queryKey: computed(() => eventsQueryKey(eventsWindow.value)),
  queryFn: async () => {
    const window = eventsWindow.value
    const { data } = await call(
      api.GET('/api/v1/events', { params: { query: eventsQueryParams(window) } }),
    )
    return { items: visibleEvents(data.items, window), truncated: data.truncated }
  },
  // Wait for the overlay so hidden calendars are never fetched (and flash) first.
  enabled: computed(() => calendars.isSuccess.value && !prefs.isPending.value && hasSources.value),
  placeholderData: keepPreviousData,
})

const mapped = computed<MappedEvent[]>(() =>
  hasSources.value
    ? (events.data.value?.items ?? [])
        .map((e) => toCalendarEvent(e, colorOf.value))
        .filter((e): e is MappedEvent => e !== null)
    : [],
)
const truncated = computed(() => hasSources.value && events.data.value?.truncated === true)
const isEmpty = computed(
  () =>
    !events.isPlaceholderData.value &&
    (events.isSuccess.value || !hasSources.value) &&
    mapped.value.length === 0,
)

/** A shorter view than the current one, offered when the window was truncated. */
const smallerView = computed<CalendarViewName | null>(() =>
  view.value === 'month' || view.value === 'agenda' ? 'week' : view.value === 'week' ? 'day' : null,
)

const title = ref('')
const selected = shallowRef<EventResponse | null>(null)
const detailsOpen = ref(false)
watch(detailsOpen, (open) => {
  if (!open) selected.value = null
})

const timeFormat = { hour: 'numeric', minute: '2-digit' } as const
const busyLabel = computed(() => t('calendar.busy'))

const calendarRef = ref<InstanceType<typeof FullCalendar> | null>(null)

const options = computed<CalendarOptions>(() => ({
  plugins: [classicTheme, dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin],
  initialView: fullCalendarViews[view.value],
  initialDate: date.value.toString(),
  headerToolbar: false,
  timeZone: timeZone.value,
  firstDay: firstDay.value,
  locale: locale.value === 'de' ? deLocale : 'en',
  height: view.value === 'agenda' ? 'auto' : '100%',
  events: mapped.value,
  eventInteractive: true,
  nowIndicator: true,
  dayMaxEvents: true,
  scrollTime: '07:30:00',
  eventTimeFormat: timeFormat,
  allDayText: t('calendar.allDay'),
  noEventsContent: t('calendar.emptyRange'),
  datesSet: (info) => {
    title.value = info.view.title
  },
  eventClick: (info: EventClickInfo) => {
    info.jsEvent.preventDefault()
    const props = info.event.extendedProps as MappedEvent['extendedProps']
    if (props.busy) return
    selected.value = props.source
    detailsOpen.value = true
  },
  eventDidMount: (info: MountInfo<EventDisplayInfo>) => {
    const props = info.event.extendedProps as MappedEvent['extendedProps']
    if (props.busy) {
      // Not interactive and nothing to open: keep it out of the tab order and name it "Busy".
      info.el.removeAttribute('tabindex')
      info.el.removeAttribute('href')
      info.el.setAttribute('aria-label', busyLabel.value)
      info.el.setAttribute('data-testid', 'busy-event')
      return
    }
    info.el.setAttribute('data-testid', 'calendar-event')
    if (!props.editable) {
      info.el.setAttribute('aria-label', `${info.event.title} (${t('calendar.readOnly')})`)
    }
  },
}))

/** Follows view and date of the URL (the options above only set the initial ones). */
watch([view, date], ([v, d]) => {
  calendarRef.value?.getApi().changeView(fullCalendarViews[v], d.toString())
})

const viewLabels = computed(() => ({
  month: t('calendar.views.month'),
  week: t('calendar.views.week'),
  day: t('calendar.views.day'),
  agenda: t('calendar.views.agenda'),
}))

/** Keyboard shortcuts: t today, ←/→ (or p/n) previous/next, m/w/d/a views. */
function onKey(event: KeyboardEvent) {
  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return
  const target = event.target as HTMLElement | null
  if (
    target?.closest('input, textarea, select, [contenteditable], [role="dialog"], [role="menu"]')
  ) {
    return
  }
  const views: Record<string, CalendarViewName> = { m: 'month', w: 'week', d: 'day', a: 'agenda' }
  if (event.key === 't') goToday()
  else if (event.key === 'n' || (event.key === 'ArrowRight' && !target?.closest('.fc'))) step(1)
  else if (event.key === 'p' || (event.key === 'ArrowLeft' && !target?.closest('.fc'))) step(-1)
  else if (views[event.key]) go({ view: views[event.key] })
  else return
  event.preventDefault()
}
onMounted(() => window.addEventListener('keydown', onKey))
onBeforeUnmount(() => window.removeEventListener('keydown', onKey))
</script>

<template>
  <div class="flex flex-col gap-4" data-testid="calendar-page">
    <div class="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
      <div class="flex items-center gap-2">
        <UiButton
          variant="secondary"
          size="sm"
          :disabled="isToday"
          data-testid="calendar-today"
          :aria-keyshortcuts="'t'"
          @click="goToday"
        >
          {{ t('calendar.today') }}
        </UiButton>
        <div class="flex items-center">
          <UiButton
            variant="ghost"
            size="icon"
            :aria-label="t('calendar.previous')"
            aria-keyshortcuts="p"
            data-testid="calendar-prev"
            @click="step(-1)"
          >
            <ChevronLeft class="size-5" aria-hidden="true" />
          </UiButton>
          <UiButton
            variant="ghost"
            size="icon"
            :aria-label="t('calendar.next')"
            aria-keyshortcuts="n"
            data-testid="calendar-next"
            @click="step(1)"
          >
            <ChevronRight class="size-5" aria-hidden="true" />
          </UiButton>
        </div>
        <h1
          class="truncate text-xl font-semibold tracking-tight sm:text-2xl"
          aria-live="polite"
          data-testid="calendar-title"
        >
          <span class="sr-only">{{ t('calendar.title') }}: </span>{{ title }}
        </h1>
        <UiSpinner
          v-if="events.isFetching.value"
          class="size-4 shrink-0 text-on-surface-muted"
          :label="t('common.loading')"
        />
      </div>
      <div
        role="radiogroup"
        :aria-label="t('calendar.viewLabel')"
        class="inline-flex self-start rounded-lg border border-border bg-surface-muted p-0.5 md:self-auto"
        data-testid="calendar-views"
      >
        <button
          v-for="v in calendarViews"
          :key="v"
          type="button"
          role="radio"
          :aria-checked="view === v"
          :aria-keyshortcuts="v.charAt(0)"
          :data-testid="`calendar-view-${v}`"
          class="h-8 rounded-md px-3 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-focus"
          :class="
            view === v
              ? 'bg-surface-raised text-on-surface shadow-sm'
              : 'text-on-surface-muted hover:text-on-surface'
          "
          @click="go({ view: v })"
        >
          {{ viewLabels[v] }}
        </button>
      </div>
    </div>

    <UiAlert v-if="truncated" variant="warning" role="status" data-testid="calendar-truncated">
      <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <span>{{ t('calendar.truncated') }}</span>
        <UiButton
          v-if="smallerView"
          variant="secondary"
          size="sm"
          @click="go({ view: smallerView })"
        >
          {{ t('calendar.showSmaller', { view: viewLabels[smallerView] }) }}
        </UiButton>
      </div>
    </UiAlert>
    <UiAlert v-if="events.isError.value" variant="danger" role="alert">
      <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <span>{{ t('calendar.loadError') }} {{ errorMessage(t, events.error.value) }}</span>
        <UiButton variant="secondary" size="sm" @click="events.refetch()">
          {{ t('common.retry') }}
        </UiButton>
      </div>
    </UiAlert>

    <EmptyState
      v-if="calendars.isSuccess.value && layers.length === 0 && !sharedWithMe"
      :title="t('calendar.noCalendarsTitle')"
      :description="t('calendar.noCalendarsText')"
      data-testid="calendar-no-calendars"
    >
      <template #icon><CalendarDays aria-hidden="true" /></template>
    </EmptyState>
    <template v-else>
      <p
        v-if="isEmpty && view !== 'agenda'"
        role="status"
        class="-mt-1 text-sm text-on-surface-muted"
        data-testid="calendar-empty"
      >
        {{ hasSources ? t('calendar.emptyRange') : t('calendar.allHidden') }}
      </p>
      <div
        class="scal-calendar min-w-0"
        :class="view === 'agenda' ? '' : 'h-[calc(100dvh-14rem)] min-h-[30rem]'"
        :data-view="view"
        data-testid="calendar"
      >
        <FullCalendar ref="calendarRef" :options="options">
          <template #eventContent="arg">
            <span v-if="arg.event.extendedProps.busy" class="scal-event-busy">
              {{ busyLabel }}
            </span>
            <span v-else class="scal-event-content">
              <span v-if="arg.timeText && !arg.event.allDay" class="scal-event-time">{{
                arg.timeText
              }}</span>
              <span class="scal-event-title">{{ arg.event.title }}</span>
              <Lock
                v-if="!arg.event.extendedProps.editable"
                class="scal-event-lock"
                aria-hidden="true"
              />
            </span>
          </template>
        </FullCalendar>
      </div>
    </template>

    <EventDetailsDialog
      v-model:open="detailsOpen"
      :event="selected"
      :calendar-name="
        selected?.sharedWithMe
          ? t('calendar.sharedWithMe')
          : layers.find((l) => l.calendar.id === selected?.calendarId)?.calendar.name
      "
      :color="selected ? (selected.sharedWithMe ? sharedColor : colorOf(selected.calendarId)) : ''"
      :time-zone="timeZone"
    />
  </div>
</template>
