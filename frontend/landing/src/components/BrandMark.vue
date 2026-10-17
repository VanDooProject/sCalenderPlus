<script setup lang="ts">
import { site } from '@/site'
</script>

<template>
  <span class="inline-flex items-center gap-2.5">
    <svg viewBox="0 0 32 32" class="size-8 shrink-0" aria-hidden="true">
      <rect width="32" height="32" rx="8" class="fill-brand-600" />
      <rect
        x="7"
        y="9"
        width="18"
        height="16"
        rx="3"
        fill="none"
        stroke="white"
        stroke-width="2.2"
      />
      <path
        d="M7 14h18M12 6.5v5M20 6.5v5"
        stroke="white"
        stroke-width="2.2"
        stroke-linecap="round"
      />
    </svg>
    <span class="text-base font-semibold tracking-tight">{{ site.brandName }}</span>
  </span>
</template>
