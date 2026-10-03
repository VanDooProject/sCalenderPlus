<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { persistLocale, supportedLocales, type Locale } from '@/i18n'

const { t, locale } = useI18n()

function onChange(event: Event) {
  const value = (event.target as HTMLSelectElement).value as Locale
  locale.value = value
  persistLocale(value)
  document.documentElement.lang = value
}
</script>

<template>
  <label class="flex items-center gap-2 text-sm text-on-surface-muted">
    <span>{{ t('nav.language') }}</span>
    <select
      data-testid="locale-switcher"
      class="rounded-md border border-border bg-surface px-2 py-1 text-on-surface"
      :value="locale"
      @change="onChange"
    >
      <option v-for="code in supportedLocales" :key="code" :value="code">
        {{ code.toUpperCase() }}
      </option>
    </select>
  </label>
</template>
