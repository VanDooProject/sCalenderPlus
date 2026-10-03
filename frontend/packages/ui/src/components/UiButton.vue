<script setup lang="ts">
withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary'
    type?: 'button' | 'submit' | 'reset'
  }>(),
  { variant: 'primary', type: 'button' },
)
</script>

<template>
  <button
    :type="type"
    class="inline-flex items-center justify-center rounded-md px-3 py-1.5 text-sm font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500 disabled:opacity-50"
    :class="
      variant === 'primary'
        ? 'bg-brand-600 text-white hover:bg-brand-700'
        : 'border border-border bg-surface text-on-surface hover:bg-surface-muted'
    "
  >
    <slot />
  </button>
</template>
