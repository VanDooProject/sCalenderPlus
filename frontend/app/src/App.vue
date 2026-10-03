<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import LocaleSwitcher from './components/LocaleSwitcher.vue'
import { useConfig } from './config'

const { t } = useI18n()
const config = useConfig()
</script>

<template>
  <div class="flex min-h-screen flex-col">
    <header class="border-b border-border bg-surface-muted">
      <div class="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-3">
        <RouterLink
          :to="{ name: 'home' }"
          class="text-lg font-semibold text-brand-600"
          data-testid="app-title"
        >
          {{ t('app.name') }}
        </RouterLink>
        <div class="flex items-center gap-4">
          <span
            v-if="config.environment !== 'production'"
            class="rounded bg-brand-100 px-2 py-0.5 text-xs text-brand-700"
            data-testid="environment-badge"
          >
            {{ t('environment', { name: config.environment }) }}
          </span>
          <LocaleSwitcher />
        </div>
      </div>
    </header>
    <main class="mx-auto w-full max-w-5xl flex-1 px-4 py-8">
      <RouterView />
    </main>
  </div>
</template>
