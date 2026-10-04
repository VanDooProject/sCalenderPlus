<script setup lang="ts">
defineProps<{ title: string; description?: string }>()
</script>

<template>
  <div
    class="flex flex-col items-center justify-center gap-3 rounded-2xl border border-dashed border-border-strong bg-surface-raised/60 px-6 py-14 text-center"
  >
    <span
      v-if="$slots.icon"
      class="flex size-12 items-center justify-center rounded-xl bg-primary-soft text-on-primary-soft [&_svg]:size-6"
    >
      <slot name="icon" />
    </span>
    <h2 class="text-base font-semibold">{{ title }}</h2>
    <p v-if="description" class="max-w-md text-sm text-on-surface-muted">{{ description }}</p>
    <div v-if="$slots.default" class="mt-2"><slot /></div>
  </div>
</template>
