<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Languages } from '@lucide/vue'
import { UiButton, UiMenu, UiMenuRadioGroup } from '@scalenderplus/ui'
import { useLocaleSwitch } from '@/composables/locale'
import { supportedLocales } from '@/i18n'

const { t } = useI18n()
const { locale, setLocale } = useLocaleSwitch()

/** Each language in its own name (endonym), as users look for their language. */
const options = supportedLocales.map((code) => ({
  value: code,
  label: new Intl.DisplayNames([code], { type: 'language' }).of(code) ?? code,
  testid: `locale-option-${code}`,
}))
const model = computed({ get: () => locale.value, set: setLocale })
</script>

<template>
  <UiMenu testid="locale-menu">
    <template #trigger>
      <UiButton
        variant="ghost"
        size="sm"
        class="h-10 px-2.5"
        :aria-label="t('nav.languageLabel', { current: locale.toUpperCase() })"
        data-testid="locale-switcher"
      >
        <Languages class="size-5" aria-hidden="true" />
        <span class="text-xs font-semibold tracking-wide" aria-hidden="true">
          {{ locale.toUpperCase() }}
        </span>
      </UiButton>
    </template>
    <UiMenuRadioGroup v-model="model" :label="t('nav.language')" :options="options" />
  </UiMenu>
</template>
