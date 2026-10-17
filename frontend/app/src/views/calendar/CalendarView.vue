<script setup lang="ts">
import { Temporal } from 'temporal-polyfill'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/vue-query'
import FullCalendar from '@fullcalendar/vue3'
import type {
  CalendarOptions,
  DateSelectInfo,
  EventClickInfo,
  EventDisplayInfo,
  EventDropInfo,
  EventResizeDoneInfo,
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
import { CalendarDays, ChevronLeft, ChevronRight, Lock, Plus, ShieldCheck } from '@lucide/vue'
import type { EventResponse } from '@scalenderplus/api-client'
import { UiAlert, UiButton, UiSpinner, useToast } from '@scalenderplus/ui'
import EmptyState from '@/components/EmptyState.vue'
import EventDetailsDialog from '@/components/calendar/EventDetailsDialog.vue'
import EventMoveDialog from '@/components/calendar/EventMoveDialog.vue'
import EventEditDialog from '@/components/calendar/EventEditDialog.vue'
import { useCalendarLayers, useSharedWithMe } from '@/composables/calendars'
import { useLinkedEvent } from '@/composables/eventLink'
import { useSession } from '@/composables/session'
import { api } from '@/api'
import { call } from '@/lib/apiError'
import { fallbackColor, sharedColor } from '@/lib/calendarColors'
import { errorMessage, isApiError } from '@/lib/errorMessages'
import { levelAtLeast } from '@/lib/calendarLevels'
import { droppedTimes, newEventValues, type EventFormValues } from '@/lib/eventForm'
import {
  calendarViews,
  isEditable,
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

const queryClient = useQueryClient()
const toast = useToast()

/** Calendars the user may add events to (`contribute` and up, not frozen), shown ones first. */
const writableCalendars = computed(() =>
  [...layers.value]
    .filter((l) => levelAtLeast(l.calendar.myLevel, 'contribute') && !l.calendar.frozen)
    .sort((a, b) => Number(a.hidden) - Number(b.hidden))
    .map((l) => ({ value: l.calendar.id, label: l.calendar.name })),
)

/** The calendar select of the dialog: an edited event's own calendar is always listed (disabled). */
const dialogCalendars = computed(() => {
  const e = editEvent.value
  if (!e || writableCalendars.value.some((c) => c.value === e.calendarId)) {
    return writableCalendars.value
  }
  const name = e.sharedWithMe
    ? t('calendar.sharedWithMe')
    : (layers.value.find((l) => l.calendar.id === e.calendarId)?.calendar.name ?? '')
  return [{ value: e.calendarId, label: name }, ...writableCalendars.value]
})

const editOpen = ref(false)
const editEvent = shallowRef<EventResponse | null>(null)
const editInitial = shallowRef<EventFormValues | null>(null)

/** Opens the event dialog for a new event (a selected range, or the next full hour today). */
function openNew(range?: { start: string; end?: string; allDay: boolean }) {
  const calendarId = writableCalendars.value[0]?.value
  if (!calendarId) {
    toast.show({ variant: 'error', title: t('event.noWritableCalendar') })
    return
  }
  const now = Temporal.Now.zonedDateTimeISO(timeZone.value)
  const day = isToday.value ? now.toPlainDate() : date.value
  const hour = isToday.value ? Math.min(now.hour + 1, 23) : 9
  const start = `${day.toString()}T${String(hour).padStart(2, '0')}:00`
  editEvent.value = null
  editInitial.value = newEventValues({
    calendarId,
    timeZone: timeZone.value,
    ...(range ?? { start, allDay: false }),
  })
  editOpen.value = true
}

function openEdit() {
  editEvent.value = selected.value
  editInitial.value = null
  detailsOpen.value = false
  editOpen.value = true
}

/** `/e/{id}` and `/e/{id}/edit` (EventLinkView) arrive as `?event={id}[&edit=1]`. */
useLinkedEvent((event, edit) => {
  if (edit && isEditable(event)) {
    editEvent.value = event
    editInitial.value = null
    editOpen.value = true
  } else {
    selected.value = event
    detailsOpen.value = true
  }
})

/**
 * `?new=1` (the onboarding checklist's "Add event"): the new event dialog once the calendars are
 * known; the parameter is removed first so closing the dialog does not reopen it.
 */
watch(
  [() => route.query.new, () => calendars.isSuccess.value],
  ([value, loaded]) => {
    if (value !== '1' || !loaded) return
    const query = { ...route.query }
    delete query.new
    void router.replace({ query })
    // After mounting: the dialog fills its form when `open` changes, not when it mounts open.
    void nextTick(() => openNew())
  },
  { immediate: true },
)

const moveOpen = ref(false)
const moveEvent = ref<EventResponse | null>(null)
function openMove() {
  moveEvent.value = selected.value
  detailsOpen.value = false
  moveOpen.value = true
}

/** Drag and resize: both times as wall times in the event's zone; failures put it back. */
async function onEventChange(info: EventDropInfo | EventResizeDoneInfo) {
  const props = info.event.extendedProps as MappedEvent['extendedProps']
  const source = props.source
  const allDay = info.event.allDay
  const start = info.event.startStr
  const fallbackEnd = allDay
    ? Temporal.PlainDate.from(start.slice(0, 10)).add({ days: 1 }).toString()
    : Temporal.PlainDateTime.from(start.slice(0, 19)).add({ hours: 1 }).toString()
  const times = droppedTimes({
    start,
    end: info.event.endStr || fallbackEnd,
    allDay,
    viewZone: timeZone.value,
    eventZone: source.start.timeZone ?? timeZone.value,
  })
  const header = { 'If-Match': source.etag ?? '*' }
  try {
    const { data } = source.recurrenceId
      ? await call(
          api.PATCH('/api/v1/events/{id}/occurrences/{recurrenceId}', {
            params: { path: { id: source.id, recurrenceId: source.recurrenceId }, header },
            body: times,
          }),
        )
      : await call(
          api.PATCH('/api/v1/events/{id}', {
            params: { path: { id: source.id }, header },
            body: times,
          }),
        )
    const warning = data.warnings?.[0]
    if (warning) {
      toast.info(
        t('event.warnings.shifted', {
          requested: warning.requested.slice(11, 16),
          resolved: warning.resolved.slice(11, 16),
        }),
      )
    } else toast.success(t('event.moved'))
  } catch (error) {
    info.revert()
    toast.show({ variant: 'error', title: errorMessage(t, error) })
    if (!isApiError(error, 'precondition_failed')) return
  }
  await queryClient.invalidateQueries({ queryKey: ['events'] })
}

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
  // New events: select a range in week/day or click a day in the month (not in the agenda).
  selectable: view.value !== 'agenda',
  selectMirror: true,
  select: (info: DateSelectInfo) => {
    calendarRef.value?.getApi().unselect()
    openNew({ start: info.startStr, end: info.endStr, allDay: info.allDay })
  },
  eventDrop: (info: EventDropInfo) => void onEventChange(info),
  eventResize: (info: EventResizeDoneInfo) => void onEventChange(info),
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
    // FullCalendar 7.1's list view puts `role="button"` events directly into a `role="list"`
    // (no list items: an ARIA error); the day heading groups them anyway.
    const parent = info.el.parentElement
    if (parent?.getAttribute('role') === 'list' && info.el.getAttribute('role') !== 'listitem') {
      parent.removeAttribute('role')
      parent.removeAttribute('aria-label')
    }
    if (props.busy) {
      // Not interactive and nothing to open: keep it out of the tab order and name it "Busy" (or
      // "<cover title> (busy)").
      info.el.removeAttribute('tabindex')
      info.el.removeAttribute('href')
      info.el.setAttribute(
        'aria-label',
        info.event.title ? t('calendar.busyCover', { title: info.event.title }) : busyLabel.value,
      )
      info.el.setAttribute('data-testid', 'busy-event')
      return
    }
    info.el.setAttribute('data-testid', 'calendar-event')
    const notes = [
      ...(props.editable ? [] : [t('calendar.readOnly')]),
      ...(props.source?.hasOverrides ? [t('permissions.customPermissions')] : []),
    ]
    if (notes.length) {
      info.el.setAttribute('aria-label', `${info.event.title} (${notes.join(', ')})`)
    }
  },
}))

/**
 * Events shorter than 45 minutes are too low in the week and day grids for a second line: time and
 * title share one line there (#56 polish), so the title is never clipped under the time.
 */
function isShort(start: Date | null, end: Date | null): boolean {
  if (!start || !end) return false
  return end.getTime() - start.getTime() < 45 * 60_000
}

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

/** Keyboard shortcuts: n new event, t today, ←/→ (or p) previous/next, m/w/d/a views. */
function onKey(event: KeyboardEvent) {
  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return
  const target = event.target as HTMLElement | null
  if (
    target?.closest('input, textarea, select, [contenteditable], [role="dialog"], [role="menu"]')
  ) {
    return
  }
  const views: Record<string, CalendarViewName> = { m: 'month', w: 'week', d: 'day', a: 'agenda' }
  if (event.key === 'n') openNew()
  else if (event.key === 't') goToday()
  else if (event.key === 'ArrowRight' && !target?.closest('.fc')) step(1)
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
            aria-keyshortcuts="ArrowRight"
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
      <div class="flex flex-wrap items-center gap-2 self-start md:self-auto">
        <UiButton
          size="sm"
          aria-keyshortcuts="n"
          data-testid="calendar-new-event"
          :disabled="writableCalendars.length === 0"
          @click="openNew()"
        >
          <Plus class="size-4" aria-hidden="true" />{{ t('event.new') }}
        </UiButton>
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
              {{ arg.event.title || busyLabel }}
            </span>
            <span
              v-else
              class="scal-event-content"
              :class="{ 'scal-event-short': isShort(arg.event.start, arg.event.end) }"
            >
              <span v-if="arg.timeText && !arg.event.allDay" class="scal-event-time">{{
                arg.timeText
              }}</span>
              <span class="scal-event-title">{{ arg.event.title }}</span>
              <ShieldCheck
                v-if="arg.event.extendedProps.source?.hasOverrides"
                class="scal-event-lock"
                role="img"
                :aria-label="t('permissions.customPermissions')"
                data-testid="event-custom-permissions"
              />
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
      :calendar-level="layers.find((l) => l.calendar.id === selected?.calendarId)?.calendar.myLevel"
      @edit="openEdit"
      @move="openMove"
    />
    <EventMoveDialog
      v-model:open="moveOpen"
      :event="moveEvent"
      :calendars="layers.map((l) => l.calendar)"
    />
    <EventEditDialog
      v-model:open="editOpen"
      :event="editEvent"
      :initial="editInitial"
      :calendars="dialogCalendars"
      :time-zone="timeZone"
    />
  </div>
</template>
