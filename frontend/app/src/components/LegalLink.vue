<script setup lang="ts">
import { computed } from 'vue'
import { useBrand } from '@/config'
import { legalRouteName, legalUrl, type LegalDocument } from '@/lib/legal'

/**
 * A link to a legal page: the deployment's own (`PUBLIC_BRAND_*_URL`) when configured, else the
 * built-in template page (`/legal/<doc>`).
 */
const props = defineProps<{ doc: LegalDocument }>()
const brand = useBrand()
const href = computed(() => legalUrl(brand, props.doc))
const linkClass =
  'rounded underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus'
</script>

<template>
  <a v-if="href" :href="href" :class="linkClass" :data-testid="`legal-${doc}`"><slot /></a>
  <RouterLink
    v-else
    :to="{ name: legalRouteName(doc) }"
    :class="linkClass"
    :data-testid="`legal-${doc}`"
  >
    <slot />
  </RouterLink>
</template>
