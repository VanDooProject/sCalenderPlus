<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useBrand } from '@/config'
import { legalDocuments } from '@/lib/legal'
import LegalLink from './LegalLink.vue'

/** Imprint, privacy policy, terms and the support address: the footer of every page. */
const { t } = useI18n()
const brand = useBrand()
</script>

<template>
  <nav :aria-label="t('legal.label')" data-testid="legal-links">
    <ul class="flex flex-wrap justify-center gap-x-4 gap-y-1">
      <li v-for="doc in legalDocuments" :key="doc">
        <LegalLink :doc="doc" class="hover:text-on-surface">{{ t(`legal.${doc}`) }}</LegalLink>
      </li>
      <li v-if="brand.supportEmail">
        <a
          :href="`mailto:${brand.supportEmail}`"
          class="rounded underline-offset-2 hover:text-on-surface hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          data-testid="legal-support"
        >
          {{ t('legal.support') }}
        </a>
      </li>
      <slot />
    </ul>
  </nav>
</template>
