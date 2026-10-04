<script setup lang="ts">
defineProps<{ title: string; description?: string }>()
</script>

<template>
  <section class="rounded-2xl border border-border bg-surface-raised shadow-xs">
    <header class="flex flex-col gap-1 border-b border-border px-5 py-4 sm:px-6">
      <h2 class="text-base font-semibold">{{ title }}</h2>
      <p v-if="description" class="text-sm text-on-surface-muted">{{ description }}</p>
    </header>
    <div class="px-5 py-5 sm:px-6"><slot /></div>
    <footer
      v-if="$slots.footer"
      class="flex flex-col-reverse gap-2 rounded-b-2xl border-t border-border bg-surface-muted px-5 py-3 sm:flex-row sm:items-center sm:justify-end sm:px-6"
    >
      <slot name="footer" />
    </footer>
  </section>
</template>
