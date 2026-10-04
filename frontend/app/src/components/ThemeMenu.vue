<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Monitor, Moon, Sun } from '@lucide/vue'
import { UiButton, UiMenu, UiMenuRadioGroup } from '@scalenderplus/ui'
import { themePreferences, useTheme, type ThemePreference } from '@/composables/theme'

const { t } = useI18n()
const { preference, resolved } = useTheme()

const options = computed(() =>
  themePreferences.map((value) => ({
    value,
    label: t(`theme.${value}`),
    testid: `theme-option-${value}`,
  })),
)
const model = computed({
  get: () => preference.value,
  set: (value: string) => (preference.value = value as ThemePreference),
})
</script>

<template>
  <UiMenu testid="theme-menu">
    <template #trigger>
      <UiButton
        variant="ghost"
        size="icon"
        :aria-label="t('theme.label', { current: t(`theme.${preference}`) })"
        data-testid="theme-toggle"
      >
        <Monitor v-if="preference === 'system'" class="size-5" aria-hidden="true" />
        <Moon v-else-if="resolved === 'dark'" class="size-5" aria-hidden="true" />
        <Sun v-else class="size-5" aria-hidden="true" />
      </UiButton>
    </template>
    <UiMenuRadioGroup v-model="model" :label="t('theme.title')" :options="options" />
  </UiMenu>
</template>
