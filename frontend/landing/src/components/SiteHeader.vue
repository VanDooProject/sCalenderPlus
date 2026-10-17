<script setup lang="ts">
import { computed } from 'vue'
import { localeTags, locales, pathFor } from '@/lib/routes'
import { appLink, usePage } from '@/site'
import BrandMark from './BrandMark.vue'

const { locale, page, t, link } = usePage()
const other = computed(() => locales.find((l) => l !== locale.value)!)
const otherPath = computed(() => pathFor(other.value, page.value ?? 'home'))
const anchors = ['features', 'pricing', 'faq'] as const

const linkClass =
  'rounded-md px-2 py-1 text-sm font-medium text-on-surface-muted hover:text-on-surface focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus'
</script>

<template>
  <header
    class="sticky top-0 z-40 border-b border-border bg-surface/90 backdrop-blur supports-[backdrop-filter]:bg-surface/75"
  >
    <div class="mx-auto flex h-16 max-w-6xl items-center gap-4 px-4 sm:px-6">
      <RouterLink
        :to="link('home')"
        class="rounded-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        :aria-label="t('nav.home')"
      >
        <BrandMark />
      </RouterLink>
      <nav :aria-label="t('nav.main')" class="ml-4 hidden md:block">
        <ul class="flex items-center gap-2">
          <li v-for="anchor in anchors" :key="anchor">
            <RouterLink :to="`${link('home')}#${anchor}`" :class="linkClass">
              {{ t(`nav.${anchor}`) }}
            </RouterLink>
          </li>
        </ul>
      </nav>
      <div class="ml-auto flex items-center gap-1 sm:gap-2">
        <RouterLink
          :to="otherPath"
          :hreflang="localeTags[other].hreflang"
          :lang="localeTags[other].hreflang"
          :class="linkClass"
          data-testid="language-switch"
        >
          {{ t('nav.switchTo') }}
        </RouterLink>
        <a :href="appLink('/login')" :class="[linkClass, 'hidden sm:inline-block']">
          {{ t('nav.signIn') }}
        </a>
        <a
          :href="appLink('/register')"
          class="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-on-primary shadow-sm hover:bg-primary-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        >
          {{ t('nav.getStarted') }}
        </a>
      </div>
    </div>
  </header>
</template>
