<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { CalendarDays, Settings, Users } from '@lucide/vue'
import { api } from '@/api'
import { useGroups } from '@/composables/groups'
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

/** The user's groups (links to their pages). */
const groups = useGroups()
const isCurrentGroup = (id: string) => route.path.startsWith(`/groups/${id}`)
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

    <div class="mt-8 flex min-h-0 flex-1 flex-col gap-8 overflow-y-auto px-3 pb-6">
      <section aria-labelledby="sidebar-calendars">
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
        <p
          v-else-if="calendars.data.value?.length === 0"
          class="px-3 text-sm text-on-surface-muted"
        >
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

      <section aria-labelledby="sidebar-groups">
        <h2
          id="sidebar-groups"
          class="px-3 pb-2 text-xs font-semibold tracking-wider text-on-surface-muted uppercase"
        >
          {{ t('sidebar.groups') }}
        </h2>
        <ul v-if="groups.isPending.value" class="flex flex-col gap-2 px-3" aria-hidden="true">
          <li v-for="i in 2" :key="i" class="h-5 animate-pulse rounded bg-surface-hover" />
        </ul>
        <p v-else-if="groups.isError.value" class="px-3 text-sm text-on-surface-muted">
          {{ t('sidebar.groupsError') }}
          <button
            type="button"
            class="font-medium text-primary underline-offset-2 hover:underline"
            @click="groups.refetch()"
          >
            {{ t('common.retry') }}
          </button>
        </p>
        <p v-else-if="groups.data.value?.length === 0" class="px-3 text-sm text-on-surface-muted">
          {{ t('sidebar.noGroups') }}
        </p>
        <ul v-else class="flex flex-col gap-0.5" data-testid="sidebar-groups">
          <li v-for="group in groups.data.value" :key="group.id">
            <RouterLink
              :to="{ name: 'group-members', params: { groupId: group.id } }"
              :aria-current="isCurrentGroup(group.id) ? 'page' : undefined"
              class="flex h-9 items-center gap-3 rounded-lg px-3 text-sm transition-colors focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus"
              :class="
                isCurrentGroup(group.id)
                  ? 'bg-surface-hover font-medium text-on-surface'
                  : 'text-on-surface hover:bg-surface-hover'
              "
              active-class=""
              exact-active-class=""
              @click="$emit('navigate')"
            >
              <span
                class="flex size-5 shrink-0 items-center justify-center rounded-md bg-primary-soft text-[0.6875rem] font-semibold text-on-primary-soft"
                aria-hidden="true"
                >{{ group.name.trim().charAt(0).toUpperCase() }}</span
              >
              <span class="truncate">{{ group.name }}</span>
            </RouterLink>
          </li>
        </ul>
      </section>
    </div>
  </div>
</template>
