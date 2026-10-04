<script setup lang="ts" generic="T extends string">
import {
  SelectContent,
  SelectIcon,
  SelectItem,
  SelectItemIndicator,
  SelectItemText,
  SelectPortal,
  SelectRoot,
  SelectTrigger,
  SelectValue,
  SelectViewport,
} from 'reka-ui'
import { useFieldControl } from '../composables/field'
import type { UiSelectOption } from '../types'

const props = defineProps<{
  options: readonly UiSelectOption<T>[]
  placeholder?: string
  id?: string
  invalid?: boolean
  disabled?: boolean
  /** Accessible name when the select is not inside a UiField. */
  ariaLabel?: string
  testid?: string
}>()
const model = defineModel<T>()
const { id, describedBy, invalid } = useFieldControl(props)
</script>

<template>
  <SelectRoot v-model="model" :disabled="disabled">
    <SelectTrigger
      :id="id"
      :aria-label="ariaLabel"
      :aria-describedby="describedBy"
      :aria-invalid="invalid || undefined"
      :data-testid="testid"
      class="flex h-10 w-full items-center justify-between gap-2 rounded-lg border bg-surface-raised px-3 text-left text-base text-on-surface shadow-xs transition-colors focus-visible:outline-2 focus-visible:-outline-offset-1 focus-visible:outline-focus disabled:opacity-55 data-[placeholder]:text-on-surface-muted sm:text-sm"
      :class="invalid ? 'border-danger' : 'border-border-strong hover:border-on-surface-muted'"
    >
      <SelectValue :placeholder="placeholder" class="truncate" />
      <SelectIcon class="text-on-surface-muted">
        <svg viewBox="0 0 20 20" fill="currentColor" class="size-4" aria-hidden="true">
          <path
            fill-rule="evenodd"
            d="M5.22 8.22a.75.75 0 0 1 1.06 0L10 11.94l3.72-3.72a.75.75 0 1 1 1.06 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L5.22 9.28a.75.75 0 0 1 0-1.06Z"
            clip-rule="evenodd"
          />
        </svg>
      </SelectIcon>
    </SelectTrigger>
    <SelectPortal>
      <SelectContent
        position="popper"
        :side-offset="4"
        class="z-50 max-h-(--reka-select-content-available-height) min-w-(--reka-select-trigger-width) animate-scale-in overflow-hidden rounded-lg border border-border bg-surface-raised text-on-surface shadow-overlay"
      >
        <SelectViewport class="p-1">
          <SelectItem
            v-for="option in options"
            :key="option.value"
            :value="option.value"
            class="relative flex h-9 cursor-default items-center rounded-md pr-3 pl-8 text-sm outline-none select-none data-[disabled]:opacity-50 data-[highlighted]:bg-surface-hover"
          >
            <SelectItemIndicator class="absolute left-2 inline-flex text-primary">
              <svg viewBox="0 0 16 16" fill="none" class="size-4" aria-hidden="true">
                <path
                  d="m3.5 8.5 3 3 6-7"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                />
              </svg>
            </SelectItemIndicator>
            <SelectItemText>{{ option.label }}</SelectItemText>
          </SelectItem>
        </SelectViewport>
      </SelectContent>
    </SelectPortal>
  </SelectRoot>
</template>
