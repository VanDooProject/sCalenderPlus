<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery } from '@tanstack/vue-query'
import { UiButton } from '@scalenderplus/ui'
import { api } from '@/api'

const { t } = useI18n()

const health = useQuery({
  queryKey: ['health', 'live'],
  queryFn: async () => {
    const { data, response } = await api.GET('/health/live')
    if (!response.ok || !data) {
      throw new Error(`Health check failed with ${response.status}`)
    }
    return data
  },
  retry: false,
})

const statusText = computed(() => {
  if (health.isPending.value) return t('home.apiStatusLoading')
  return health.isSuccess.value ? t('home.apiStatusOk') : t('home.apiStatusDown')
})
</script>

<template>
  <section class="space-y-4">
    <h1 class="text-2xl font-semibold">{{ t('home.title') }}</h1>
    <p class="text-on-surface-muted">{{ t('home.intro') }}</p>
    <p class="flex items-center gap-3 text-sm">
      <span>{{ t('home.apiStatus') }}:</span>
      <strong data-testid="api-status">{{ statusText }}</strong>
      <UiButton variant="secondary" @click="health.refetch()">{{ t('home.retry') }}</UiButton>
    </p>
  </section>
</template>
