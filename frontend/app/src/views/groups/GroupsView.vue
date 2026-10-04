<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Users } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import PageHeader from '@/components/PageHeader.vue'

const { t } = useI18n()
</script>

<template>
  <div class="mx-auto flex max-w-5xl flex-col gap-6">
    <PageHeader :title="t('groups.title')" :description="t('groups.description')" />
    <EmptyState :title="t('groups.comingTitle')" :description="t('groups.comingText')">
      <template #icon><Users aria-hidden="true" /></template>
    </EmptyState>
  </div>
</template>
