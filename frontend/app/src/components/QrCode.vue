<script setup lang="ts">
import { computed } from 'vue'
import { encode } from 'uqr'

/**
 * A QR code drawn as SVG on the client (the 2FA secret never goes to a third-party service).
 * Dark modules on a white quiet zone, also in dark mode, so every scanner reads it.
 */
const props = defineProps<{ value: string; label: string }>()

const qr = computed(() => encode(props.value, { ecc: 'M', border: 2 }))
const path = computed(() =>
  qr.value.data
    .flatMap((row, y) => row.map((dark, x) => (dark ? `M${x} ${y}h1v1h-1z` : '')))
    .join(''),
)
</script>

<template>
  <svg
    :viewBox="`0 0 ${qr.size} ${qr.size}`"
    role="img"
    :aria-label="label"
    shape-rendering="crispEdges"
    class="rounded-lg bg-white"
    data-testid="qr-code"
  >
    <path :d="path" fill="#111827" />
  </svg>
</template>
