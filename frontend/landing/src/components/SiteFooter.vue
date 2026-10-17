<script setup lang="ts">
import { legalUrl } from '@/lib/legal'
import { legalPages } from '@/lib/routes'
import { appLink, site, usePage } from '@/site'
import BrandMark from './BrandMark.vue'

/** Footer of every page: product links, the legal pages (the operator's own, if configured), bot. */
const { t, link } = usePage()
const year = new Date().getFullYear()
const anchors = ['features', 'pricing', 'faq'] as const

const linkClass =
  'rounded text-sm text-on-surface-muted underline-offset-2 hover:text-on-surface hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus'
</script>

<template>
  <footer class="border-t border-border bg-surface-muted" data-testid="site-footer">
    <div class="mx-auto grid max-w-6xl gap-10 px-4 py-12 sm:px-6 md:grid-cols-4">
      <div class="flex flex-col gap-3 md:col-span-2">
        <BrandMark />
        <p class="max-w-sm text-sm text-on-surface-muted">{{ t('footer.tagline') }}</p>
      </div>
      <nav :aria-label="t('footer.product')">
        <h2 class="text-sm font-semibold">{{ t('footer.product') }}</h2>
        <ul class="mt-3 flex flex-col gap-2">
          <li v-for="anchor in anchors" :key="anchor">
            <RouterLink :to="`${link('home')}#${anchor}`" :class="linkClass">
              {{ t(`nav.${anchor}`) }}
            </RouterLink>
          </li>
          <li>
            <a :href="appLink('/login')" :class="linkClass">{{ t('nav.signIn') }}</a>
          </li>
        </ul>
      </nav>
      <nav :aria-label="t('footer.legal')">
        <h2 class="text-sm font-semibold">{{ t('footer.legal') }}</h2>
        <ul class="mt-3 flex flex-col gap-2">
          <li v-for="doc in legalPages" :key="doc">
            <a
              v-if="legalUrl(site, doc)"
              :href="legalUrl(site, doc)!"
              :class="linkClass"
              :data-testid="`footer-${doc}`"
            >
              {{ t(`footer.${doc}`) }}
            </a>
            <RouterLink v-else :to="link(doc)" :class="linkClass" :data-testid="`footer-${doc}`">
              {{ t(`footer.${doc}`) }}
            </RouterLink>
          </li>
          <li>
            <RouterLink :to="link('bot')" :class="linkClass" data-testid="footer-bot">
              {{ t('footer.bot') }}
            </RouterLink>
          </li>
          <li v-if="site.supportEmail">
            <a :href="`mailto:${site.supportEmail}`" :class="linkClass">{{
              t('footer.contact')
            }}</a>
          </li>
        </ul>
      </nav>
    </div>
    <div class="border-t border-border">
      <p class="mx-auto max-w-6xl px-4 py-4 text-xs text-on-surface-muted sm:px-6">
        {{ t('footer.copyright', { year }) }}
      </p>
    </div>
  </footer>
</template>
