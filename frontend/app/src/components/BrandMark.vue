<script setup lang="ts">
import { useBrand } from '@/config'

withDefaults(defineProps<{ showName?: boolean }>(), { showName: true })
const brand = useBrand()
</script>

<template>
  <span class="inline-flex items-center gap-2.5">
    <!-- The deployment's logo (/config.json); decorative next to the visible name. -->
    <img
      v-if="brand.logoUrl"
      :src="brand.logoUrl"
      :alt="showName ? '' : brand.name"
      class="h-8 w-auto max-w-32 shrink-0 object-contain"
      data-testid="brand-logo"
    />
    <svg v-else viewBox="0 0 32 32" class="size-8 shrink-0" aria-hidden="true">
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
      <path d="M16 17.2v5M13.5 19.7h5" stroke="white" stroke-width="2" stroke-linecap="round" />
    </svg>
    <span v-if="showName" class="text-base font-semibold tracking-tight" data-testid="app-title">
      {{ brand.name }}
    </span>
  </span>
</template>
