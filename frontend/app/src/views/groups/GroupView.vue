<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { ArrowLeft, SearchX, Snowflake } from '@lucide/vue'
import { UiAlert, UiButton } from '@scalenderplus/ui'
import EmptyState from '@/components/EmptyState.vue'
import RoleBadge from '@/components/groups/RoleBadge.vue'
import { provideCurrentGroup, roleOf } from '@/composables/currentGroup'
import { useGroup } from '@/composables/groups'
import { useSession } from '@/composables/session'
import { isApiError } from '@/lib/errorMessages'
import { canManageInvites } from '@/lib/groupRoles'

const { t } = useI18n()
const route = useRoute()
const { user } = useSession()

const id = computed(() => String(route.params.groupId ?? ''))
const query = useGroup(id)
const group = computed(() => query.data.value?.group ?? null)
const myRole = computed(() => roleOf(group.value))

provideCurrentGroup({
  id,
  group,
  etag: computed(() => query.data.value?.etag ?? null),
  myRole,
  isBillingOwner: computed(() => !!user.value && group.value?.billingOwnerId === user.value.id),
})

const notFound = computed(() => isApiError(query.error.value, 'not_found'))

const tabs = computed(() => [
  { name: 'group-members', label: 'groups.tabs.members', testid: 'tab-members' },
  ...(canManageInvites(myRole.value)
    ? [{ name: 'group-invites', label: 'groups.tabs.invites', testid: 'tab-invites' }]
    : []),
  { name: 'group-settings', label: 'groups.tabs.settings', testid: 'tab-settings' },
])
</script>

<template>
  <div class="mx-auto flex max-w-4xl flex-col gap-6">
    <RouterLink
      :to="{ name: 'groups' }"
      class="inline-flex w-fit items-center gap-1.5 rounded-md text-sm font-medium text-on-surface-muted hover:text-on-surface focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
      data-testid="group-back"
    >
      <ArrowLeft class="size-4" aria-hidden="true" />{{ t('groups.back') }}
    </RouterLink>

    <div v-if="query.isPending.value" class="flex flex-col gap-3" aria-hidden="true">
      <div class="h-8 w-56 animate-pulse rounded bg-surface-hover" />
      <div class="h-5 w-80 animate-pulse rounded bg-surface-hover" />
    </div>

    <template v-else-if="notFound">
      <h1 class="sr-only">{{ t('groups.notFoundTitle') }}</h1>
      <EmptyState :title="t('groups.notFoundTitle')" :description="t('groups.notFoundText')">
        <template #icon><SearchX aria-hidden="true" /></template>
      </EmptyState>
    </template>

    <div
      v-else-if="query.isError.value || !group"
      class="flex flex-col items-start gap-3 rounded-2xl border border-border bg-surface-raised p-5 text-sm"
      role="alert"
    >
      <h1 class="sr-only">{{ t('groups.title') }}</h1>
      <p>{{ t('groups.loadError') }}</p>
      <UiButton variant="secondary" size="sm" @click="query.refetch()">
        {{ t('common.retry') }}
      </UiButton>
    </div>

    <template v-else>
      <header class="flex flex-col gap-2">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-2">
          <h1 class="text-2xl font-semibold tracking-tight break-words" data-testid="group-title">
            {{ group.name }}
          </h1>
          <span class="sr-only">{{ t('groups.yourRole') }}:</span>
          <RoleBadge :role="group.myRole" />
        </div>
        <p v-if="group.description" class="text-sm text-on-surface-muted">
          {{ group.description }}
        </p>
        <p class="text-sm text-on-surface-muted" data-testid="group-member-count">
          {{ t('groups.memberCount', group.memberCount) }}
        </p>
      </header>

      <UiAlert v-if="group.frozen" variant="warning" data-testid="group-frozen">
        <template #icon><Snowflake aria-hidden="true" /></template>
        {{ t('groups.frozenHint') }}
      </UiAlert>

      <nav :aria-label="t('groups.tabs.label')" class="border-b border-border">
        <ul class="-mb-px flex gap-6 overflow-x-auto">
          <li v-for="tab in tabs" :key="tab.name">
            <RouterLink
              :to="{ name: tab.name, params: { groupId: id } }"
              :data-testid="tab.testid"
              class="inline-flex h-11 items-center border-b-2 border-transparent text-sm font-medium whitespace-nowrap text-on-surface-muted transition-colors hover:border-border-strong hover:text-on-surface focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus"
              exact-active-class="!border-primary !text-on-surface"
            >
              {{ t(tab.label) }}
            </RouterLink>
          </li>
        </ul>
      </nav>

      <RouterView />
    </template>
  </div>
</template>
