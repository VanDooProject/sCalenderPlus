<script setup lang="ts">
import { useI18n } from 'vue-i18n'

withDefaults(defineProps<{ showName?: boolean }>(), { showName: true })
const { t } = useI18n()
</script>

<template>
  <span class="inline-flex items-center gap-2.5">
    <svg viewBox="0 0 32 32" class="size-8 shrink-0" aria-hidden="true">
      <rect width="32" height="32" rx="8" class="fill-brand-600" />
      <rect
        x="7"
        y="9"
        width="18"
        height="16"
        rx="3"
        fill="none"
        stroke="white"
        stroke-width="2.2"
      />
      <path
        d="M7 14h18M12 6.5v5M20 6.5v5"
        stroke="white"
        stroke-width="2.2"
        stroke-linecap="round"
      />
      <path d="M16 17.2v5M13.5 19.7h5" stroke="white" stroke-width="2" stroke-linecap="round" />
    </svg>
    <span v-if="showName" class="text-base font-semibold tracking-tight" data-testid="app-title">
      {{ t('app.name') }}
    </span>
  </span>
</template>
