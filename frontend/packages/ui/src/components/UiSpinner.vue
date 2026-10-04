<script setup lang="ts">
defineProps<{
  /** Accessible text; without it the spinner is decorative (e.g. inside a busy button). */
  label?: string
}>()
</script>

<template>
  <span
    :role="label ? 'status' : undefined"
    class="inline-flex items-center gap-2"
    :aria-hidden="label ? undefined : 'true'"
  >
    <svg class="size-full animate-spin" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle
        cx="12"
        cy="12"
        r="9.5"
        stroke="currentColor"
        stroke-opacity="0.25"
        stroke-width="3"
      />
      <path
        d="M21.5 12a9.5 9.5 0 0 0-9.5-9.5"
        stroke="currentColor"
        stroke-width="3"
        stroke-linecap="round"
      />
    </svg>
    <span v-if="label" class="sr-only">{{ label }}</span>
  </span>
</template>
