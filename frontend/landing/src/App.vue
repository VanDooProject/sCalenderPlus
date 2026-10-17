<script setup lang="ts">
import { computed } from 'vue'
import { useHead } from '@unhead/vue'
import SiteFooter from '@/components/SiteFooter.vue'
import SiteHeader from '@/components/SiteHeader.vue'
import { pageHead } from '@/lib/head'
import { site, usePage } from '@/site'

const { locale, page, t } = usePage()
const metaKey = computed(() => page.value ?? 'notFound')

useHead(
  computed(() =>
    pageHead(site, locale.value, page.value, {
      title: t(`meta.${metaKey.value}.title`),
      description: t(`meta.${metaKey.value}.description`),
      ogAlt: t('meta.ogAlt'),
    }),
  ),
)
</script>

<template>
  <div class="flex min-h-dvh flex-col">
    <a
      href="#main"
      class="sr-only z-50 rounded-md bg-primary px-4 py-2 font-medium text-on-primary focus:not-sr-only focus:fixed focus:top-3 focus:left-3"
    >
      {{ t('nav.skip') }}
    </a>
    <SiteHeader />
    <main id="main" class="flex-1" tabindex="-1">
      <RouterView />
    </main>
    <SiteFooter />
  </div>
</template>
