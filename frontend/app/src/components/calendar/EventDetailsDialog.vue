<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Clock,
  Lock,
  MapPin,
  Pencil,
  Repeat,
  FolderInput,
  Share2,
  ShieldCheck,
  UserRound,
} from '@lucide/vue'
import type { EventResponse } from '@scalenderplus/api-client'
import { UiButton, UiDialog } from '@scalenderplus/ui'
import EventAccessExplain from '@/components/calendar/EventAccessExplain.vue'
import EventPermissions from '@/components/calendar/EventPermissions.vue'
import { levelAtLeast } from '@/lib/calendarLevels'
import { isEditable } from '@/lib/calendarView'
import { describeRule } from '@/lib/recurrenceText'

/** Details of an event; `edit` opens the event dialog when the user may change it. */
const props = defineProps<{
  event: EventResponse | null
  calendarName?: string
  color: string
  timeZone: string
  /** My level on the event's calendar: managers may explain other people's access. */
  calendarLevel?: string
}>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ edit: []; move: [] }>()

const { t, locale } = useI18n()

/** "Mon, 2 Nov · 18:00 – 20:00" in the user's zone; all-day events as dates (end exclusive). */
const when = computed(() => {
  const e = props.event
  if (!e) return ''
  if (e.allDay && e.start.date && e.end.date) {
    const format = new Intl.DateTimeFormat(locale.value, {
      weekday: 'short',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    })
    const start = new Date(`${e.start.date}T00:00:00Z`)
    const last = new Date(Date.parse(`${e.end.date}T00:00:00Z`) - 86_400_000)
    return last > start ? format.formatRange(start, last) : format.format(start)
  }
  if (!e.start.utc || !e.end.utc) return ''
  const format = new Intl.DateTimeFormat(locale.value, {
    weekday: 'short',
    day: 'numeric',
    month: 'long',
    hour: 'numeric',
    minute: '2-digit',
    timeZone: props.timeZone,
  })
  return format.formatRange(new Date(e.start.utc), new Date(e.end.utc))
})

const level = computed(() => {
  const e = props.event
  if (!e) return ''
  return isEditable(e) ? t('calendar.level.edit') : t('calendar.level.read')
})

// Tabs: details, permissions (event level `manage`), access ("Why can I see this?").
type Tab = 'details' | 'permissions' | 'access'
const tab = ref<Tab>('details')
watch([open, () => props.event?.id], () => (tab.value = 'details'))
const canManage = computed(() => props.event?.myLevel === 'manage')
const canExplainOthers = computed(() => levelAtLeast(props.calendarLevel ?? 'none', 'manage'))
const tabs = computed<{ id: Tab; label: string }[]>(() => [
  { id: 'details', label: t('permissions.tabs.details') },
  ...(canManage.value
    ? [{ id: 'permissions' as const, label: t('permissions.tabs.permissions') }]
    : []),
  {
    id: 'access',
    label: canExplainOthers.value
      ? t('permissions.tabs.accessManager')
      : t('permissions.tabs.access'),
  },
])
const tabIds = (id: Tab) => ({ tab: `event-tab-${id}`, panel: `event-panel-${id}` })

/** Arrow keys move between the tabs (WAI-ARIA tabs pattern, automatic activation). */
function onTabKey(event: KeyboardEvent) {
  const ids = tabs.value.map((x) => x.id)
  const at = ids.indexOf(tab.value)
  const next =
    event.key === 'ArrowRight'
      ? (at + 1) % ids.length
      : event.key === 'ArrowLeft'
        ? (at - 1 + ids.length) % ids.length
        : -1
  if (next < 0) return
  event.preventDefault()
  tab.value = ids[next]!
  document.getElementById(tabIds(tab.value).tab)?.focus()
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="event?.title ?? ''"
    :close-label="t('common.close')"
    :size="tab === 'details' ? 'md' : 'lg'"
    testid="event-details"
  >
    <div
      v-if="event"
      role="tablist"
      :aria-label="t('permissions.tabs.label')"
      class="mb-4 flex gap-1 border-b border-border"
      @keydown="onTabKey"
    >
      <button
        v-for="item in tabs"
        :id="tabIds(item.id).tab"
        :key="item.id"
        type="button"
        role="tab"
        :aria-selected="tab === item.id"
        :aria-controls="tabIds(item.id).panel"
        :tabindex="tab === item.id ? 0 : -1"
        class="-mb-px border-b-2 px-3 py-2 text-sm font-medium focus-visible:outline-2 focus-visible:outline-focus"
        :class="
          tab === item.id
            ? 'border-primary text-on-surface'
            : 'border-transparent text-on-surface-muted hover:text-on-surface'
        "
        :data-testid="`event-tab-${item.id}`"
        @click="tab = item.id"
      >
        {{ item.label }}
      </button>
    </div>
    <div
      v-if="event && tab === 'permissions' && canManage"
      :id="tabIds('permissions').panel"
      role="tabpanel"
      :aria-labelledby="tabIds('permissions').tab"
    >
      <EventPermissions :event="event" />
    </div>
    <div
      v-else-if="event && tab === 'access'"
      :id="tabIds('access').panel"
      role="tabpanel"
      :aria-labelledby="tabIds('access').tab"
    >
      <EventAccessExplain :event="event" :can-explain-others="canExplainOthers" />
    </div>
    <dl
      v-else-if="event"
      :id="tabIds('details').panel"
      role="tabpanel"
      :aria-labelledby="tabIds('details').tab"
      class="flex flex-col gap-3 text-sm"
    >
      <div class="flex items-start gap-3">
        <dt class="sr-only">{{ t('calendar.details.when') }}</dt>
        <Clock class="mt-0.5 size-4 shrink-0 text-on-surface-muted" aria-hidden="true" />
        <dd data-testid="event-details-when">
          {{ when }}
          <span
            v-if="event.recurrence"
            class="ml-1 inline-flex items-center gap-1 text-on-surface-muted"
          >
            <Repeat class="size-3.5" aria-hidden="true" />{{
              event.recurrence.rrule
                ? describeRule(t, event.recurrence.rrule)
                : t('calendar.details.recurring')
            }}
          </span>
        </dd>
      </div>
      <div class="flex items-start gap-3">
        <dt class="sr-only">{{ t('calendar.details.calendar') }}</dt>
        <svg viewBox="0 0 16 16" class="mt-0.5 size-4 shrink-0" aria-hidden="true">
          <rect x="2" y="2" width="12" height="12" rx="3.5" :fill="color" />
        </svg>
        <dd class="flex items-center gap-1.5">
          <Share2
            v-if="event.sharedWithMe"
            class="size-3.5 text-on-surface-muted"
            aria-hidden="true"
          />
          <RouterLink
            v-if="!event.sharedWithMe && calendarName"
            :to="{ name: 'calendar-settings', params: { calendarId: event.calendarId } }"
            class="rounded-sm underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
            :aria-label="t('calendar.settingsOf', { name: calendarName })"
            data-testid="event-calendar-settings"
            >{{ calendarName }}</RouterLink
          >
          <template v-else>{{ calendarName }}</template>
        </dd>
      </div>
      <div v-if="event.location" class="flex items-start gap-3">
        <dt class="sr-only">{{ t('calendar.details.location') }}</dt>
        <MapPin class="mt-0.5 size-4 shrink-0 text-on-surface-muted" aria-hidden="true" />
        <dd>{{ event.location }}</dd>
      </div>
      <div v-if="event.createdBy?.displayName" class="flex items-start gap-3">
        <dt class="sr-only">{{ t('calendar.details.createdBy') }}</dt>
        <UserRound class="mt-0.5 size-4 shrink-0 text-on-surface-muted" aria-hidden="true" />
        <dd>{{ t('calendar.details.createdByName', { name: event.createdBy.displayName }) }}</dd>
      </div>
      <div class="flex items-start gap-3">
        <dt class="sr-only">{{ t('calendar.details.access') }}</dt>
        <component
          :is="isEditable(event) ? Pencil : Lock"
          class="mt-0.5 size-4 shrink-0 text-on-surface-muted"
          aria-hidden="true"
        />
        <div>
          <dd data-testid="event-details-level">{{ level }}</dd>
          <dd>
            <button
              type="button"
              class="rounded-sm text-primary underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-focus"
              data-testid="event-details-why"
              @click="tab = 'access'"
            >
              {{ t('permissions.why') }}
            </button>
          </dd>
          <dd
            v-if="event.hasOverrides"
            class="mt-0.5 flex items-center gap-1 text-on-surface-muted"
            data-testid="event-details-custom"
          >
            <ShieldCheck class="size-3.5" aria-hidden="true" />{{
              t('permissions.customPermissions')
            }}
          </dd>
          <dd
            v-if="!isEditable(event)"
            class="mt-0.5 text-on-surface-muted"
            data-testid="event-details-readonly"
          >
            {{ t('event.readOnlyReason') }}
          </dd>
        </div>
      </div>
      <div v-if="event.description" class="border-t border-border pt-3">
        <dt class="sr-only">{{ t('calendar.details.description') }}</dt>
        <dd class="whitespace-pre-line text-on-surface">{{ event.description }}</dd>
      </div>
    </dl>
    <template v-if="event && isEditable(event) && tab === 'details'" #footer>
      <UiButton variant="secondary" data-testid="event-details-edit" @click="emit('edit')">
        <Pencil class="size-4" aria-hidden="true" />{{ t('event.edit') }}
      </UiButton>
      <UiButton
        v-if="canManage"
        variant="secondary"
        data-testid="event-details-move"
        @click="emit('move')"
      >
        <FolderInput class="size-4" aria-hidden="true" />{{ t('event.move.open') }}
      </UiButton>
    </template>
  </UiDialog>
</template>
