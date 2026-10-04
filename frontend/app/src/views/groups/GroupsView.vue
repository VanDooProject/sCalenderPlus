<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ChevronRight, Plus, Snowflake, Users } from '@lucide/vue'
import { UiButton } from '@scalenderplus/ui'
import EmptyState from '@/components/EmptyState.vue'
import PageHeader from '@/components/PageHeader.vue'
import CreateGroupDialog from '@/components/groups/CreateGroupDialog.vue'
import RoleBadge from '@/components/groups/RoleBadge.vue'
import { useGroups } from '@/composables/groups'

const { t } = useI18n()
const groups = useGroups()
const creating = ref(false)
</script>

<template>
  <div class="mx-auto flex max-w-5xl flex-col gap-6">
    <PageHeader :title="t('groups.title')" :description="t('groups.description')">
      <template v-if="groups.data.value?.length" #actions>
        <UiButton data-testid="create-group" @click="creating = true">
          <Plus class="size-4" aria-hidden="true" />{{ t('groups.create') }}
        </UiButton>
      </template>
    </PageHeader>

    <ul v-if="groups.isPending.value" class="grid gap-3 sm:grid-cols-2" aria-hidden="true">
      <li v-for="i in 2" :key="i" class="h-28 animate-pulse rounded-2xl bg-surface-hover" />
    </ul>

    <div
      v-else-if="groups.isError.value"
      class="flex flex-col items-start gap-3 rounded-2xl border border-border bg-surface-raised p-5 text-sm"
      role="alert"
    >
      <p>{{ t('groups.loadError') }}</p>
      <UiButton variant="secondary" size="sm" @click="groups.refetch()">
        {{ t('common.retry') }}
      </UiButton>
    </div>

    <EmptyState
      v-else-if="groups.data.value?.length === 0"
      :title="t('groups.emptyTitle')"
      :description="t('groups.emptyText')"
    >
      <template #icon><Users aria-hidden="true" /></template>
      <UiButton data-testid="create-group" @click="creating = true">
        <Plus class="size-4" aria-hidden="true" />{{ t('groups.create') }}
      </UiButton>
    </EmptyState>

    <ul v-else class="grid gap-3 sm:grid-cols-2" data-testid="group-list">
      <li v-for="group in groups.data.value" :key="group.id">
        <RouterLink
          :to="{ name: 'group-members', params: { groupId: group.id } }"
          class="group flex h-full items-start gap-4 rounded-2xl border border-border bg-surface-raised p-5 shadow-xs transition-colors hover:border-border-strong hover:bg-surface-hover/40 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          :data-testid="`group-card-${group.id}`"
        >
          <span
            class="flex size-11 shrink-0 items-center justify-center rounded-xl bg-primary-soft text-base font-semibold text-on-primary-soft"
            aria-hidden="true"
          >
            {{ group.name.trim().charAt(0).toUpperCase() }}
          </span>
          <span class="flex min-w-0 flex-1 flex-col gap-1.5">
            <span class="flex items-center gap-2">
              <span class="truncate font-semibold" data-testid="group-card-name">{{
                group.name
              }}</span>
              <Snowflake
                v-if="group.frozen"
                class="size-4 shrink-0 text-on-surface-muted"
                :aria-label="t('groups.frozen')"
              />
            </span>
            <span v-if="group.description" class="line-clamp-2 text-sm text-on-surface-muted">
              {{ group.description }}
            </span>
            <span class="mt-1 flex flex-wrap items-center gap-2 text-sm text-on-surface-muted">
              <span class="sr-only">{{ t('groups.yourRole') }}:</span>
              <RoleBadge :role="group.myRole" />
              <span aria-hidden="true">·</span>
              <span data-testid="group-card-members">{{
                t('groups.memberCount', group.memberCount)
              }}</span>
            </span>
          </span>
          <ChevronRight
            class="mt-3 size-5 shrink-0 text-on-surface-muted transition-transform group-hover:translate-x-0.5"
            aria-hidden="true"
          />
        </RouterLink>
      </li>
    </ul>

    <CreateGroupDialog v-model:open="creating" />
  </div>
</template>
