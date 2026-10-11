<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { Check, Palette, Settings } from '@lucide/vue'
import { UiMenu, UiMenuItem, UiMenuSeparator } from '@scalenderplus/ui'
import {
  useCalendarLayers,
  useSharedWithMe,
  useUpdateCalendarPrefs,
  type CalendarLayer,
} from '@/composables/calendars'
import { calendarPalette, sharedColor } from '@/lib/calendarColors'

const { t } = useI18n()
const router = useRouter()
const emit = defineEmits<{ navigate: [] }>()

function openSettings(layer: CalendarLayer) {
  emit('navigate')
  void router.push({ name: 'calendar-settings', params: { calendarId: layer.calendar.id } })
}
const { calendars, layers } = useCalendarLayers()
const update = useUpdateCalendarPrefs()
const sharedWithMe = useSharedWithMe()

const palette = calendarPalette

/** The stored overlay color (null = the calendar's color). */
function overlayColor(layer: CalendarLayer): string | null {
  return layer.color === layer.calendar.color ? null : layer.color
}

function toggle(layer: CalendarLayer) {
  update.mutate({
    calendarId: layer.calendar.id,
    hidden: !layer.hidden,
    color: overlayColor(layer),
  })
}

function setColor(layer: CalendarLayer, color: string | null) {
  update.mutate({ calendarId: layer.calendar.id, hidden: layer.hidden, color })
}
</script>

<template>
  <ul v-if="calendars.isPending.value" class="flex flex-col gap-2 px-3" aria-hidden="true">
    <li v-for="i in 3" :key="i" class="h-5 animate-pulse rounded bg-surface-hover" />
  </ul>
  <p v-else-if="calendars.isError.value" class="px-3 text-sm text-on-surface-muted">
    {{ t('sidebar.calendarsError') }}
    <button
      type="button"
      class="font-medium text-primary underline-offset-2 hover:underline"
      @click="calendars.refetch()"
    >
      {{ t('common.retry') }}
    </button>
  </p>
  <template v-else>
    <p v-if="layers.length === 0" class="px-3 pb-1 text-sm text-on-surface-muted">
      {{ t('sidebar.noCalendars') }}
    </p>
    <ul class="flex flex-col gap-0.5" data-testid="sidebar-calendars">
      <li
        v-for="layer in layers"
        :key="layer.calendar.id"
        class="group flex h-9 items-center gap-1 rounded-lg pr-1 text-sm hover:bg-surface-hover"
        :data-testid="`calendar-layer-${layer.calendar.id}`"
      >
        <label class="flex h-full min-w-0 flex-1 cursor-pointer items-center gap-3 pl-3">
          <input
            type="checkbox"
            class="peer sr-only"
            :checked="!layer.hidden"
            :data-testid="`calendar-toggle-${layer.calendar.id}`"
            @change="toggle(layer)"
          />
          <svg
            viewBox="0 0 16 16"
            class="size-4 shrink-0 rounded-[0.3rem] peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-focus"
            aria-hidden="true"
          >
            <rect
              x="1"
              y="1"
              width="14"
              height="14"
              rx="3.5"
              :fill="layer.hidden ? 'transparent' : layer.color"
              :stroke="layer.color"
              stroke-width="2"
            />
            <path
              v-if="!layer.hidden"
              d="M4.5 8.2 7 10.6l4.6-5"
              fill="none"
              stroke="white"
              stroke-width="1.8"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
          <span class="truncate" :class="layer.hidden ? 'text-on-surface-muted' : ''">
            {{ layer.calendar.name }}
          </span>
        </label>
        <UiMenu align="start" :testid="`calendar-colors-${layer.calendar.id}`">
          <template #trigger>
            <button
              type="button"
              class="inline-flex size-7 shrink-0 items-center justify-center rounded-md text-on-surface-muted opacity-100 hover:bg-surface hover:text-on-surface focus-visible:outline-2 focus-visible:outline-focus lg:opacity-0 lg:group-hover:opacity-100 lg:focus-visible:opacity-100 lg:data-[state=open]:opacity-100"
              :aria-label="t('calendar.colorOf', { name: layer.calendar.name })"
            >
              <Palette class="size-4" aria-hidden="true" />
            </button>
          </template>
          <UiMenuItem
            :data-testid="`calendar-settings-${layer.calendar.id}`"
            @select="openSettings(layer)"
          >
            <Settings class="size-3" aria-hidden="true" />
            <span class="flex-1">{{ t('calendar.settings') }}</span>
          </UiMenuItem>
          <UiMenuSeparator />
          <UiMenuItem @select="setColor(layer, null)">
            <svg viewBox="0 0 12 12" class="size-3" aria-hidden="true">
              <rect width="12" height="12" rx="3.5" :fill="layer.calendar.color ?? sharedColor" />
            </svg>
            <span class="flex-1">{{ t('calendar.colors.default') }}</span>
            <Check v-if="overlayColor(layer) === null" aria-hidden="true" />
          </UiMenuItem>
          <UiMenuSeparator />
          <UiMenuItem
            v-for="color in palette"
            :key="color.value"
            @select="setColor(layer, color.value)"
          >
            <svg viewBox="0 0 12 12" class="size-3" aria-hidden="true">
              <rect width="12" height="12" rx="3.5" :fill="color.value" />
            </svg>
            <span class="flex-1">{{ t(`calendar.colors.${color.name}`) }}</span>
            <Check v-if="overlayColor(layer) === color.value" aria-hidden="true" />
          </UiMenuItem>
        </UiMenu>
      </li>
      <li class="flex h-9 items-center rounded-lg pr-1 text-sm hover:bg-surface-hover">
        <label class="flex h-full min-w-0 flex-1 cursor-pointer items-center gap-3 pl-3">
          <input
            v-model="sharedWithMe"
            type="checkbox"
            class="peer sr-only"
            data-testid="calendar-toggle-shared"
          />
          <svg
            viewBox="0 0 16 16"
            class="size-4 shrink-0 rounded-[0.3rem] peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-focus"
            aria-hidden="true"
          >
            <rect
              x="1"
              y="1"
              width="14"
              height="14"
              rx="3.5"
              :fill="sharedWithMe ? sharedColor : 'transparent'"
              :stroke="sharedColor"
              stroke-width="2"
            />
            <path
              v-if="sharedWithMe"
              d="M4.5 8.2 7 10.6l4.6-5"
              fill="none"
              stroke="white"
              stroke-width="1.8"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
          <span class="truncate" :class="sharedWithMe ? '' : 'text-on-surface-muted'">
            {{ t('calendar.sharedWithMe') }}
          </span>
        </label>
      </li>
    </ul>
  </template>
</template>
