<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ExternalLink } from '@lucide/vue'
import { UiAlert } from '@scalenderplus/ui'
import { useBrand } from '@/config'
import { legalSections, legalUrl, type LegalDocument } from '@/lib/legal'

/**
 * `/legal/imprint|privacy|terms`: the deployment's own page when `PUBLIC_BRAND_*_URL` is set (a
 * link out, the footer links there directly), else a minimal built-in template that says so and
 * tells the operator what to fill in. The privacy template describes what the software stores.
 */
const props = defineProps<{ doc: LegalDocument }>()
const { t } = useI18n()
const brand = useBrand()

const url = computed(() => legalUrl(brand, props.doc))
const sections = computed(() => legalSections[props.doc])
const app = computed(() => brand.name)
</script>

<template>
  <article class="flex flex-col gap-6" :data-testid="`legal-page-${doc}`">
    <h1 class="text-2xl font-semibold tracking-tight">{{ t(`legal.pages.${doc}.title`) }}</h1>

    <p v-if="url">
      {{ t('legal.external', { app }) }}
      <a
        :href="url"
        class="inline-flex items-center gap-1 font-medium text-primary underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        data-testid="legal-external"
      >
        {{ t(`legal.pages.${doc}.title`) }}
        <ExternalLink class="size-3.5" aria-hidden="true" />
      </a>
    </p>

    <template v-else>
      <UiAlert variant="warning" :title="t('legal.template.title')" data-testid="legal-template">
        {{ t('legal.template.text', { app }) }}
        <code class="font-mono text-xs">{{ t(`legal.pages.${doc}.variable`) }}</code
        >.
      </UiAlert>
      <section v-for="section in sections" :key="section" class="flex flex-col gap-1.5">
        <h2 class="text-base font-semibold">{{ t(`legal.pages.${doc}.${section}.title`) }}</h2>
        <p class="text-sm leading-relaxed text-on-surface-muted">
          {{ t(`legal.pages.${doc}.${section}.text`, { app }) }}
        </p>
      </section>
      <p v-if="brand.supportEmail" class="text-sm">
        {{ t('legal.contact') }}
        <a
          :href="`mailto:${brand.supportEmail}`"
          class="font-medium text-primary underline-offset-2 hover:underline"
          >{{ brand.supportEmail }}</a
        >
      </p>
    </template>
  </article>
</template>
