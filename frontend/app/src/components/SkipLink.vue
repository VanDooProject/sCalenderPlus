<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <a
    href="#main"
    class="sr-only z-[70] rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-on-primary focus:not-sr-only focus:fixed focus:top-3 focus:left-3"
  >
    {{ t('a11y.skipToContent') }}
  </a>
</template>
