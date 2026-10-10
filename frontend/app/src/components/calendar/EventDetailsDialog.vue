<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Clock, Lock, MapPin, Pencil, Repeat, Share2, UserRound } from '@lucide/vue'
import type { EventResponse } from '@scalenderplus/api-client'
import { UiDialog } from '@scalenderplus/ui'
import { isEditable } from '@/lib/calendarView'

/** Read-only details of an event (editing follows with the event dialog). */
const props = defineProps<{
  event: EventResponse | null
  calendarName?: string
  color: string
  timeZone: string
}>()
const open = defineModel<boolean>('open', { default: false })

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
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="event?.title ?? ''"
    :close-label="t('common.close')"
    size="md"
    testid="event-details"
  >
    <dl v-if="event" class="flex flex-col gap-3 text-sm">
      <div class="flex items-start gap-3">
        <dt class="sr-only">{{ t('calendar.details.when') }}</dt>
        <Clock class="mt-0.5 size-4 shrink-0 text-on-surface-muted" aria-hidden="true" />
        <dd data-testid="event-details-when">
          {{ when }}
          <span
            v-if="event.recurrence"
            class="ml-1 inline-flex items-center gap-1 text-on-surface-muted"
          >
            <Repeat class="size-3.5" aria-hidden="true" />{{ t('calendar.details.recurring') }}
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
          {{ calendarName }}
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
        <dd data-testid="event-details-level">{{ level }}</dd>
      </div>
      <div v-if="event.description" class="border-t border-border pt-3">
        <dt class="sr-only">{{ t('calendar.details.description') }}</dt>
        <dd class="whitespace-pre-line text-on-surface">{{ event.description }}</dd>
      </div>
    </dl>
  </UiDialog>
</template>
