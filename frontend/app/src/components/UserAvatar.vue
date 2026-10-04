<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ name: string }>()

const initials = computed(() => {
  const parts = props.name.trim().split(/\s+/).filter(Boolean)
  const letters = parts.length > 1 ? [parts[0]![0], parts.at(-1)![0]] : [props.name.trim()[0]]
  return letters.join('').toUpperCase() || '?'
})
</script>

<template>
  <span
    class="inline-flex size-8 shrink-0 items-center justify-center rounded-full bg-primary-soft text-xs font-semibold text-on-primary-soft ring-1 ring-border"
    aria-hidden="true"
  >
    {{ initials }}
  </span>
</template>
