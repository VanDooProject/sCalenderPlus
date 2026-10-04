<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { UiButton } from '@scalenderplus/ui'
import BrandMark from '@/components/BrandMark.vue'

const { t } = useI18n()
</script>

<template>
  <main
    id="main"
    class="flex min-h-dvh flex-col items-center justify-center gap-6 px-4 text-center"
  >
    <BrandMark :show-name="false" />
    <p class="text-sm font-semibold text-primary">404</p>
    <h1 class="text-3xl font-semibold tracking-tight">{{ t('notFound.title') }}</h1>
    <p class="max-w-sm text-on-surface-muted">{{ t('notFound.text') }}</p>
    <UiButton as-child>
      <RouterLink :to="{ name: 'calendar' }">{{ t('notFound.back') }}</RouterLink>
    </UiButton>
  </main>
</template>
