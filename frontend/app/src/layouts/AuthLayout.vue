<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import BrandMark from '@/components/BrandMark.vue'
import EnvironmentBadge from '@/components/EnvironmentBadge.vue'
import LocaleMenu from '@/components/LocaleMenu.vue'
import SkipLink from '@/components/SkipLink.vue'
import ThemeMenu from '@/components/ThemeMenu.vue'

const { t } = useI18n()
</script>

<template>
  <SkipLink />
  <div class="relative flex min-h-dvh flex-col overflow-hidden">
    <!-- Decorative brand glow behind the card. -->
    <div
      class="pointer-events-none absolute inset-x-0 -top-40 -z-10 h-96 bg-radial from-brand-200/50 to-transparent to-70% dark:from-brand-700/25"
      aria-hidden="true"
    />
    <header class="flex h-16 items-center justify-between gap-2 px-4 sm:px-6">
      <RouterLink
        :to="{ name: 'login' }"
        class="rounded-lg focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-focus"
      >
        <BrandMark />
      </RouterLink>
      <div class="flex items-center gap-1 sm:gap-2">
        <EnvironmentBadge />
        <LocaleMenu />
        <ThemeMenu />
      </div>
    </header>
    <main
      id="main"
      class="flex flex-1 items-start justify-center px-4 pt-6 pb-16 sm:items-center sm:pt-0"
    >
      <div
        class="w-full max-w-md rounded-2xl border border-border bg-surface-raised p-6 shadow-raised sm:p-8"
      >
        <RouterView />
      </div>
    </main>
    <footer class="px-4 pb-6 text-center text-xs text-on-surface-muted">
      {{ t('app.tagline') }}
    </footer>
  </div>
</template>
