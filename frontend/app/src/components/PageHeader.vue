<script setup lang="ts">
defineProps<{ title: string; description?: string }>()
</script>

<template>
  <header class="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
    <div class="flex flex-col gap-1">
      <h1 class="text-2xl font-semibold tracking-tight">{{ title }}</h1>
      <p v-if="description" class="text-sm text-on-surface-muted">{{ description }}</p>
    </div>
    <div v-if="$slots.actions" class="flex items-center gap-2"><slot name="actions" /></div>
  </header>
</template>
