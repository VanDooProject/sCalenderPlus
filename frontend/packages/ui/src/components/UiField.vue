<script setup lang="ts">
import { computed, useId } from 'vue'
import { provideField } from '../composables/field'

const props = defineProps<{
  label: string
  /** Help text below the control. */
  hint?: string
  /** Validation messages (client-side or from the problem's `errors`). */
  errors?: readonly string[] | string | null
  required?: boolean
  /** Id of the control; generated when absent. */
  id?: string
  /** Visually hide the label (still announced). */
  hideLabel?: boolean
}>()

const generated = useId()
const id = computed(() => props.id ?? `field-${generated}`)
const messages = computed(() =>
  props.errors == null ? [] : typeof props.errors === 'string' ? [props.errors] : props.errors,
)
const invalid = computed(() => messages.value.length > 0)
const hintId = computed(() => `${id.value}-hint`)
const errorId = computed(() => `${id.value}-error`)
const describedBy = computed(
  () =>
    [props.hint ? hintId.value : null, invalid.value ? errorId.value : null]
      .filter(Boolean)
      .join(' ') || undefined,
)

provideField({ id, describedBy, invalid, required: computed(() => props.required) })
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <div class="flex items-baseline justify-between gap-2">
      <label
        :for="id"
        class="text-sm font-medium text-on-surface"
        :class="hideLabel ? 'sr-only' : ''"
      >
        {{ label }}
      </label>
      <slot name="label-end" />
    </div>
    <slot :id="id" :described-by="describedBy" :invalid="invalid" />
    <p v-if="hint" :id="hintId" class="text-sm text-on-surface-muted">{{ hint }}</p>
    <div v-if="invalid" :id="errorId" class="flex flex-col gap-0.5" data-testid="field-error">
      <p
        v-for="message in messages"
        :key="message"
        class="flex items-start gap-1.5 text-sm text-on-danger-soft"
      >
        <svg
          viewBox="0 0 20 20"
          fill="currentColor"
          class="mt-0.5 size-4 shrink-0"
          aria-hidden="true"
        >
          <path
            fill-rule="evenodd"
            d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z"
            clip-rule="evenodd"
          />
        </svg>
        <span>{{ message }}</span>
      </p>
    </div>
  </div>
</template>
