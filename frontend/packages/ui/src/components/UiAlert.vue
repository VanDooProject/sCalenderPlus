<script setup lang="ts">
withDefaults(
  defineProps<{
    variant?: 'info' | 'success' | 'warning' | 'danger'
    title?: string
    /** `alert` announces it immediately (form errors); `status` politely. */
    role?: 'alert' | 'status'
  }>(),
  { variant: 'info' },
)

const variants = {
  info: 'bg-info-soft text-on-info-soft border-brand-200/60 dark:border-brand-700/50',
  success: 'bg-success-soft text-on-success-soft border-on-success-soft/15',
  warning: 'bg-warning-soft text-on-warning-soft border-on-warning-soft/15',
  danger: 'bg-danger-soft text-on-danger-soft border-on-danger-soft/15',
} as const
</script>

<template>
  <div
    :role="role"
    class="flex gap-3 rounded-xl border px-4 py-3 text-sm"
    :class="variants[variant]"
  >
    <span class="mt-0.5 shrink-0 [&_svg]:size-4.5"><slot name="icon" /></span>
    <div class="flex min-w-0 flex-1 flex-col gap-1">
      <p v-if="title" class="font-semibold">{{ title }}</p>
      <div class="leading-relaxed"><slot /></div>
    </div>
  </div>
</template>
