<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { CalendarDays, Settings, Users } from '@lucide/vue'
import { api } from '@/api'
import { call } from '@/lib/apiError'
import BrandMark from './BrandMark.vue'

defineEmits<{ navigate: [] }>()

const { t } = useI18n()
const route = useRoute()

const nav = [
  { to: { name: 'calendar' }, prefix: '/calendar', label: 'nav.calendar', icon: CalendarDays },
  { to: { name: 'groups' }, prefix: '/groups', label: 'nav.groups', icon: Users },
  { to: { name: 'settings-profile' }, prefix: '/settings', label: 'nav.settings', icon: Settings },
] as const

/** A section is current on its own pages and below (`/settings/security` → Settings). */
const isCurrent = (prefix: string) => route.path === prefix || route.path.startsWith(`${prefix}/`)

/** Visible calendars (toggling and colors per user follow with the calendar views). */
const calendars = useQuery({
  queryKey: ['calendars'],
  queryFn: async () => (await call(api.GET('/api/v1/calendars'))).data.items,
})
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex h-16 shrink-0 items-center px-5">
      <RouterLink
        :to="{ name: 'calendar' }"
        class="rounded-lg focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-focus"
        @click="$emit('navigate')"
      >
        <BrandMark />
      </RouterLink>
    </div>

    <nav :aria-label="t('nav.main')" class="px-3">
      <ul class="flex flex-col gap-0.5">
        <li v-for="item in nav" :key="item.prefix">
          <RouterLink
            :to="item.to"
            :data-testid="`nav${item.prefix.replace('/', '-')}`"
            :aria-current="isCurrent(item.prefix) ? 'page' : undefined"
            class="flex h-10 items-center gap-3 rounded-lg px-3 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus"
            :class="
              isCurrent(item.prefix)
                ? 'bg-primary-soft text-on-primary-soft'
                : 'text-on-surface-muted hover:bg-surface-hover hover:text-on-surface'
            "
            active-class=""
            exact-active-class=""
            @click="$emit('navigate')"
          >
            <component :is="item.icon" class="size-5 shrink-0" aria-hidden="true" />
            {{ t(item.label) }}
          </RouterLink>
        </li>
      </ul>
    </nav>

    <section class="mt-8 min-h-0 flex-1 overflow-y-auto px-3" aria-labelledby="sidebar-calendars">
      <h2
        id="sidebar-calendars"
        class="px-3 pb-2 text-xs font-semibold tracking-wider text-on-surface-muted uppercase"
      >
        {{ t('sidebar.calendars') }}
      </h2>
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
      <p v-else-if="calendars.data.value?.length === 0" class="px-3 text-sm text-on-surface-muted">
        {{ t('sidebar.noCalendars') }}
      </p>
      <ul v-else class="flex flex-col gap-0.5" data-testid="sidebar-calendars">
        <li
          v-for="calendar in calendars.data.value"
          :key="calendar.id"
          class="flex h-9 items-center gap-3 rounded-lg px-3 text-sm"
        >
          <svg viewBox="0 0 12 12" class="size-3 shrink-0" aria-hidden="true">
            <rect width="12" height="12" rx="3.5" :fill="calendar.color ?? 'currentColor'" />
          </svg>
          <span class="truncate">{{ calendar.name }}</span>
        </li>
      </ul>
    </section>
  </div>
</template>
