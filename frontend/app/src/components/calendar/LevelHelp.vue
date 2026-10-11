<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { calendarLevels } from '@/lib/calendarLevels'

/** What each calendar level allows, in plain words (a disclosure below the level pickers). */
const { t } = useI18n()
</script>

<template>
  <details class="rounded-lg border border-border px-4 py-3 text-sm" data-testid="level-help">
    <summary class="cursor-pointer font-medium">{{ t('calendarSettings.levels.explain') }}</summary>
    <dl class="mt-3 grid gap-x-4 gap-y-2 sm:grid-cols-[max-content_1fr]">
      <template v-for="level in calendarLevels" :key="level">
        <dt class="font-medium">{{ t(`calendarSettings.levels.${level}.label`) }}</dt>
        <dd class="text-on-surface-muted">{{ t(`calendarSettings.levels.${level}.hint`) }}</dd>
      </template>
    </dl>
  </details>
</template>
