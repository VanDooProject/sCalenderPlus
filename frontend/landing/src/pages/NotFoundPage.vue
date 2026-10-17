<script setup lang="ts">
import { pathFor } from '@/lib/routes'
import { usePage } from '@/site'

/** Prerendered as 404.html; Caddy serves it for unknown paths (status 404). */
const { t } = usePage()
</script>

<template>
  <div class="mx-auto flex max-w-xl flex-col items-start gap-4 px-4 py-24 sm:px-6">
    <h1 class="text-3xl font-bold tracking-tight">{{ t('notFound.title') }}</h1>
    <p class="text-lg text-on-surface-muted">{{ t('notFound.text') }}</p>
    <RouterLink
      :to="pathFor('en', 'home')"
      class="rounded-lg bg-primary px-4 py-2.5 font-semibold text-on-primary hover:bg-primary-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
    >
      {{ t('notFound.home') }}
    </RouterLink>
  </div>
</template>
