<script setup lang="ts">
import { computed } from 'vue'
import { botToken } from '@/lib/siteConfig'
import { site, usePage } from '@/site'

/**
 * `/bot`: the importer's documentation page that its user agent links to
 * (docs/architecture/llm-import.md §4): what it fetches, robots.txt, rate limits, opt-out, contact.
 */
const { t } = usePage()
const sections = ['what', 'robots', 'rate', 'processing'] as const
const robotsRules = computed(() => `User-agent: ${botToken(site.botUserAgent)}\nDisallow: /`)
</script>

<template>
  <article
    class="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-12 sm:px-6 md:py-16"
    data-testid="bot-page"
  >
    <h1 class="text-3xl font-bold tracking-tight">{{ t('bot.title') }}</h1>
    <p class="text-lg leading-relaxed text-on-surface-muted">{{ t('bot.lead') }}</p>

    <div class="flex flex-col gap-1.5">
      <h2 class="text-lg font-semibold">{{ t('bot.userAgent') }}</h2>
      <pre
        class="overflow-x-auto rounded-lg border border-border bg-surface-muted p-4 font-mono text-sm"
        data-testid="bot-user-agent"
      ><code>{{ site.botUserAgent }}</code></pre>
    </div>

    <section v-for="section in sections" :key="section" class="flex flex-col gap-1.5">
      <h2 class="text-lg font-semibold">{{ t(`bot.sections.${section}.title`) }}</h2>
      <p class="leading-relaxed text-on-surface-muted">{{ t(`bot.sections.${section}.text`) }}</p>
    </section>

    <section class="flex flex-col gap-1.5">
      <h2 class="text-lg font-semibold">{{ t('bot.sections.block.title') }}</h2>
      <p class="leading-relaxed text-on-surface-muted">{{ t('bot.sections.block.text') }}</p>
      <pre
        class="overflow-x-auto rounded-lg border border-border bg-surface-muted p-4 font-mono text-sm"
        data-testid="bot-robots"
      ><code>{{ robotsRules }}</code></pre>
    </section>

    <section class="flex flex-col gap-1.5">
      <h2 class="text-lg font-semibold">{{ t('bot.sections.contact.title') }}</h2>
      <p class="leading-relaxed text-on-surface-muted">
        {{ t('bot.sections.contact.text') }}
        <a
          v-if="site.supportEmail"
          :href="`mailto:${site.supportEmail}`"
          class="font-medium text-primary underline-offset-2 hover:underline"
          data-testid="bot-contact"
          >{{ site.supportEmail }}</a
        >
        <span v-else class="font-medium text-on-surface" data-testid="bot-contact">{{
          t('bot.contactPlaceholder')
        }}</span>
      </p>
    </section>
  </article>
</template>
