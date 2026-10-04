<script setup lang="ts">
import {
  DropdownMenuItemIndicator,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
} from 'reka-ui'

/** A labelled group of mutually exclusive menu choices (theme, language). */
defineProps<{
  label?: string
  options: readonly { value: string; label: string; testid?: string }[]
}>()
const model = defineModel<string>()
</script>

<template>
  <DropdownMenuLabel
    v-if="label"
    class="px-2.5 pt-1.5 pb-1 text-xs font-medium text-on-surface-muted"
  >
    {{ label }}
  </DropdownMenuLabel>
  <DropdownMenuRadioGroup v-model="model">
    <DropdownMenuRadioItem
      v-for="option in options"
      :key="option.value"
      :value="option.value"
      :data-testid="option.testid"
      class="relative flex h-9 cursor-default items-center gap-2.5 rounded-lg pr-2.5 pl-8 text-sm outline-none select-none data-[highlighted]:bg-surface-hover"
    >
      <DropdownMenuItemIndicator class="absolute left-2.5 inline-flex text-primary">
        <svg viewBox="0 0 16 16" fill="none" class="size-4" aria-hidden="true">
          <path
            d="m3.5 8.5 3 3 6-7"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
      </DropdownMenuItemIndicator>
      <slot name="option" :option="option">{{ option.label }}</slot>
    </DropdownMenuRadioItem>
  </DropdownMenuRadioGroup>
</template>
