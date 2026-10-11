<script setup lang="ts">
import { computed, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { calendarPalette } from '@/lib/calendarColors'

/** The calendar color as a radio group of swatches (a stored color outside the palette stays). */
const props = defineProps<{ label: string; disabled?: boolean }>()
const model = defineModel<string>({ required: true })
const { t } = useI18n()
const name = `color-${useId()}`

const options = computed(() => {
  const known = calendarPalette.some((c) => c.value === model.value.toLowerCase())
  return [
    ...calendarPalette.map((c) => ({ value: c.value, label: t(`calendar.colors.${c.name}`) })),
    ...(known || !model.value ? [] : [{ value: model.value, label: model.value }]),
  ]
})
</script>

<template>
  <fieldset :disabled="props.disabled" class="flex flex-col gap-2">
    <legend class="mb-2 text-sm font-medium">{{ label }}</legend>
    <div class="flex flex-wrap gap-2" data-testid="calendar-color">
      <label
        v-for="option in options"
        :key="option.value"
        class="relative inline-flex size-8 cursor-pointer items-center justify-center rounded-full has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-55 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-focus"
        :style="{ backgroundColor: option.value }"
        :title="option.label"
      >
        <input
          v-model="model"
          type="radio"
          class="sr-only"
          :name="name"
          :value="option.value"
          :aria-label="option.label"
          :data-testid="`calendar-color-${option.value.slice(1)}`"
        />
        <svg
          v-if="model.toLowerCase() === option.value"
          viewBox="0 0 16 16"
          class="size-4"
          aria-hidden="true"
        >
          <path
            d="M3.5 8.5 6.5 11.5l6-7"
            fill="none"
            stroke="white"
            stroke-width="2.2"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
      </label>
    </div>
  </fieldset>
</template>
