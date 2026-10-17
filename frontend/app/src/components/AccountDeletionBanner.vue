<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Clock } from '@lucide/vue'
import { formatPurgeDate } from '@/lib/accountDeletion'

/** Shown on every page while the account is scheduled for deletion; links to where it is cancelled. */
defineProps<{ purgeAt: string }>()
const { t, locale } = useI18n()
</script>

<template>
  <div
    class="flex flex-col gap-2 border-b border-on-danger-soft/15 bg-danger-soft px-4 py-2.5 text-sm text-on-danger-soft sm:flex-row sm:items-center sm:gap-3 lg:px-8"
    role="status"
    data-testid="account-deletion-banner"
  >
    <p class="flex flex-1 items-start gap-2">
      <Clock class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <span>{{ t('accountDeletion.banner', { date: formatPurgeDate(purgeAt, locale) }) }}</span>
    </p>
    <RouterLink
      :to="{ name: 'settings-security' }"
      class="self-start rounded-md px-2 py-1 font-semibold underline underline-offset-2 hover:bg-on-danger-soft/10 focus-visible:outline-2 focus-visible:outline-focus sm:self-auto"
    >
      {{ t('accountDeletion.bannerAction') }}
    </RouterLink>
  </div>
</template>
