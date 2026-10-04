<script setup lang="ts">
import { computed } from 'vue'
import { Primitive } from 'reka-ui'
import UiSpinner from './UiSpinner.vue'

const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
    size?: 'sm' | 'md' | 'lg' | 'icon'
    type?: 'button' | 'submit' | 'reset'
    /** Shows a spinner, disables the button and sets `aria-busy`. */
    loading?: boolean
    disabled?: boolean
    /** Full width. */
    block?: boolean
    /** Render the child (e.g. a RouterLink) with the button styles instead of a `<button>`. */
    asChild?: boolean
  }>(),
  { variant: 'primary', size: 'md', type: 'button' },
)

const variants = {
  primary: 'bg-primary text-on-primary shadow-sm hover:bg-primary-hover',
  secondary:
    'border border-border-strong bg-surface-raised text-on-surface shadow-sm hover:bg-surface-hover',
  ghost: 'text-on-surface hover:bg-surface-hover',
  danger: 'bg-danger text-on-danger shadow-sm hover:bg-danger-hover',
} as const

const sizes = {
  sm: 'h-8 gap-1.5 px-3 text-sm',
  md: 'h-10 gap-2 px-4 text-sm',
  lg: 'h-11 gap-2 px-5 text-base',
  icon: 'size-10 shrink-0',
} as const

const classes = computed(() => [
  'inline-flex select-none items-center justify-center rounded-lg font-medium whitespace-nowrap transition-colors',
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus',
  'disabled:pointer-events-none disabled:opacity-55 aria-disabled:pointer-events-none aria-disabled:opacity-55',
  variants[props.variant],
  sizes[props.size],
  props.block ? 'w-full' : '',
])
</script>

<template>
  <Primitive
    :as="asChild ? undefined : 'button'"
    :as-child="asChild"
    :type="asChild ? undefined : type"
    :disabled="asChild ? undefined : disabled || loading"
    :aria-busy="loading || undefined"
    :class="classes"
  >
    <UiSpinner v-if="loading" class="size-4" />
    <slot />
  </Primitive>
</template>
