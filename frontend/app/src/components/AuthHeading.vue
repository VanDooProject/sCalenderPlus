<script setup lang="ts">
defineProps<{ title: string; description?: string }>()
</script>

<template>
  <div class="mb-6 flex flex-col gap-1.5">
    <span
      v-if="$slots.icon"
      class="mb-2 flex size-11 items-center justify-center rounded-xl bg-primary-soft text-on-primary-soft [&_svg]:size-5.5"
    >
      <slot name="icon" />
    </span>
    <h1 class="text-2xl font-semibold tracking-tight">{{ title }}</h1>
    <p v-if="description" class="text-sm leading-relaxed text-on-surface-muted">
      {{ description }}
    </p>
  </div>
</template>
