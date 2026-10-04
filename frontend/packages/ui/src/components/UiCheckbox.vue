<script setup lang="ts">
import { useId } from 'vue'
import { CheckboxIndicator, CheckboxRoot } from 'reka-ui'

const props = defineProps<{
  label: string
  description?: string
  id?: string
  disabled?: boolean
}>()
const model = defineModel<boolean>({ default: false })
const generated = useId()
const id = props.id ?? `checkbox-${generated}`
</script>

<template>
  <div class="flex items-start gap-3">
    <CheckboxRoot
      :id="id"
      v-model="model"
      :disabled="disabled"
      :aria-describedby="description ? `${id}-description` : undefined"
      class="mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-md border border-border-strong bg-surface-raised text-on-primary transition-colors hover:border-on-surface-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:opacity-55 data-[state=checked]:border-primary data-[state=checked]:bg-primary"
    >
      <CheckboxIndicator>
        <svg viewBox="0 0 16 16" fill="none" class="size-3.5" aria-hidden="true">
          <path
            d="m3.5 8.5 3 3 6-7"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
      </CheckboxIndicator>
    </CheckboxRoot>
    <div class="flex flex-col">
      <label :for="id" class="text-sm font-medium text-on-surface select-none">{{ label }}</label>
      <p v-if="description" :id="`${id}-description`" class="text-sm text-on-surface-muted">
        {{ description }}
      </p>
    </div>
  </div>
</template>
