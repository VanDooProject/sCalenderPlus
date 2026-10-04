<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { CalendarDays } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useSession } from '@/composables/session'

const { t } = useI18n()
const { user } = useSession()
</script>

<template>
  <div class="mx-auto flex max-w-5xl flex-col gap-6">
    <PageHeader
      :title="t('calendar.title')"
      :description="user ? t('calendar.greeting', { name: user.displayName }) : undefined"
    />
    <EmptyState
      :title="t('calendar.comingTitle')"
      :description="t('calendar.comingText')"
      data-testid="calendar-placeholder"
    >
      <template #icon><CalendarDays aria-hidden="true" /></template>
    </EmptyState>
  </div>
</template>
