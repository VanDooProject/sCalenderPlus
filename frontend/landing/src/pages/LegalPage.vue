<script setup lang="ts">
import { computed } from 'vue'
import { Download, ExternalLink } from '@lucide/vue'
import { dpaPlaceholderPath, legalUrl, legalVariables } from '@/lib/legal'
import type { LegalPage } from '@/lib/routes'
import { site, usePage } from '@/site'

/**
 * `/legal/imprint|privacy|terms|dpa`: the operator's own page when configured at build time
 * (`VITE_*_URL`, then this page only links there), else a template with [placeholders] that says so.
 * The DPA page always offers the download: the operator's file (`VITE_DPA_URL`) or a placeholder.
 */
const { page, t, tm } = usePage()
const doc = computed(() => page.value as LegalPage)
const key = (rest: string) => `legal.pages.${doc.value}.${rest}`
const url = computed(() => legalUrl(site, doc.value))
// In message order: the base sections, then any an edition adds (its locale overlay).
const sections = computed(() => Object.keys(tm<Record<string, unknown>>(key('sections'))))
const dpaHref = computed(() => site.dpaUrl ?? dpaPlaceholderPath)
</script>

<template>
  <article
    class="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-12 sm:px-6 md:py-16"
    :data-testid="`legal-${doc}`"
  >
    <h1 class="text-3xl font-bold tracking-tight">{{ t(key('title')) }}</h1>

    <p v-if="url">
      {{ t('legal.external') }}
      <a
        :href="url"
        class="inline-flex items-center gap-1 font-medium text-primary underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        data-testid="legal-external"
      >
        {{ t(key('title')) }}
        <ExternalLink class="size-3.5" aria-hidden="true" />
      </a>
    </p>

    <template v-else>
      <p class="text-lg text-on-surface-muted">{{ t(key('lead')) }}</p>
      <aside
        class="rounded-lg border border-border bg-warning-soft p-4 text-sm text-on-warning-soft"
        data-testid="legal-template"
      >
        <p class="font-semibold">{{ t('legal.template.title') }}</p>
        <p class="mt-1">{{ t('legal.template.text', { variable: legalVariables[doc] }) }}</p>
      </aside>

      <section v-for="section in sections" :key="section" class="flex flex-col gap-1.5">
        <h2 class="text-lg font-semibold">{{ t(key(`sections.${section}.title`)) }}</h2>
        <p class="leading-relaxed text-on-surface-muted">
          {{ t(key(`sections.${section}.text`)) }}
        </p>
      </section>

      <div v-if="doc === 'dpa'" class="flex flex-col items-start gap-2">
        <a
          :href="dpaHref"
          download
          class="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2.5 font-semibold text-on-primary hover:bg-primary-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          data-testid="dpa-download"
        >
          <Download class="size-4" aria-hidden="true" />
          {{ t('legal.pages.dpa.download') }}
        </a>
        <p v-if="!site.dpaUrl" class="text-sm text-on-surface-muted">
          {{ t('legal.pages.dpa.placeholder', { variable: legalVariables.dpa }) }}
        </p>
      </div>

      <p v-if="site.supportEmail" class="text-sm">
        {{ t('legal.contact') }}
        <a
          :href="`mailto:${site.supportEmail}`"
          class="font-medium text-primary underline-offset-2 hover:underline"
          >{{ site.supportEmail }}</a
        >
      </p>
    </template>
  </article>
</template>
