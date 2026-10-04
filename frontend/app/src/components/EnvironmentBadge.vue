<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useConfig } from '@/config'

const { t } = useI18n()
const config = useConfig()
</script>

<template>
  <span
    v-if="config.environment !== 'production'"
    class="hidden rounded-full border border-warning-soft bg-warning-soft px-2.5 py-0.5 text-xs font-medium text-on-warning-soft sm:inline-block"
    data-testid="environment-badge"
  >
    {{ t('environment', { name: config.environment }) }}
  </span>
</template>
