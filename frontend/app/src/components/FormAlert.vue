<script setup lang="ts">
import { CircleAlert } from '@lucide/vue'
import { UiAlert } from '@scalenderplus/ui'

/** The form-level error of a failed submit, announced immediately. */
defineProps<{ message: string | null }>()
</script>

<template>
  <div aria-live="assertive">
    <UiAlert v-if="message" variant="danger" role="alert" data-testid="form-error">
      <template #icon><CircleAlert aria-hidden="true" /></template>
      {{ message }}
    </UiAlert>
  </div>
</template>
