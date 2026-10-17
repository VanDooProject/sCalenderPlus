<script setup lang="ts">
import { computed, ref } from 'vue'
import { usePage } from '@/site'

/**
 * The hero's illustration of per-entry permissions: one calendar, seen by different people. Each
 * entry has its own level per viewer (`demo.events[].access`): edit, read, free_busy (shows "Busy"
 * or the entry's cover title, never its real title) or none (not shown at all). Prerendered for the
 * first viewer; the buttons switch after hydration.
 */
type Level = 'edit' | 'read' | 'free_busy' | 'none'
interface DemoEvent {
  title: string
  when: string
  cover?: string
  access: Record<string, Level>
}

const { t, tm } = usePage()
const viewers = computed(() => tm<{ id: string; label: string }[]>('demo.viewers'))
const events = computed(() => tm<DemoEvent[]>('demo.events'))
const selected = ref<string | null>(null)
const viewer = computed(() => selected.value ?? viewers.value[0]!.id)

const visible = computed(() =>
  events.value
    .map((event) => ({ event, level: event.access[viewer.value] ?? 'none' }))
    .filter((entry) => entry.level !== 'none'),
)
const hiddenCount = computed(() => events.value.length - visible.value.length)

const badge: Record<Exclude<Level, 'none'>, string> = {
  edit: 'bg-success-soft text-on-success-soft',
  read: 'bg-info-soft text-on-info-soft',
  free_busy: 'bg-warning-soft text-on-warning-soft',
}
</script>

<template>
  <figure
    class="rounded-2xl border border-border bg-surface-raised p-5 shadow-overlay sm:p-6"
    data-testid="access-demo"
  >
    <figcaption class="text-sm font-semibold">{{ t('demo.title') }}</figcaption>
    <div
      class="mt-3 flex flex-wrap gap-2"
      role="group"
      :aria-label="t('demo.viewAs')"
      data-testid="demo-viewers"
    >
      <button
        v-for="v in viewers"
        :key="v.id"
        type="button"
        :aria-pressed="v.id === viewer"
        class="rounded-full border px-3 py-1.5 text-sm font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        :class="
          v.id === viewer
            ? 'border-primary bg-primary text-on-primary'
            : 'border-border-strong hover:bg-surface-hover'
        "
        @click="selected = v.id"
      >
        {{ v.label }}
      </button>
    </div>
    <ul class="mt-5 flex flex-col gap-2.5" aria-live="polite">
      <li
        v-for="{ event, level } in visible"
        :key="event.title"
        class="flex items-center gap-3 rounded-lg border border-border px-3 py-2.5"
        :class="level === 'free_busy' ? 'bg-surface-muted' : 'bg-surface'"
        data-testid="demo-event"
      >
        <span class="w-20 shrink-0 text-xs font-medium text-on-surface-muted tabular-nums">
          {{ event.when }}
        </span>
        <span
          class="min-w-0 flex-1 truncate text-sm font-medium"
          :class="{ italic: level === 'free_busy' }"
        >
          {{ level === 'free_busy' ? (event.cover ?? t('demo.busy')) : event.title }}
        </span>
        <span
          class="shrink-0 rounded-full px-2 py-0.5 text-xs font-semibold"
          :class="badge[level as Exclude<Level, 'none'>]"
        >
          {{ t(`demo.levels.${level}`) }}
        </span>
      </li>
    </ul>
    <p v-if="hiddenCount > 0" class="mt-3 text-xs text-on-surface-muted" data-testid="demo-hidden">
      {{ t('demo.hidden', { count: hiddenCount }) }}
    </p>
  </figure>
</template>
