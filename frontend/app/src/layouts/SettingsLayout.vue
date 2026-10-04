<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/PageHeader.vue'

const { t } = useI18n()

const tabs = [
  { to: { name: 'settings-profile' }, label: 'settings.profile.title', testid: 'tab-profile' },
  { to: { name: 'settings-security' }, label: 'settings.security.title', testid: 'tab-security' },
] as const
</script>

<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-6">
    <PageHeader :title="t('nav.settings')" :description="t('settings.description')" />
    <nav :aria-label="t('nav.settings')" class="border-b border-border">
      <ul class="-mb-px flex gap-6">
        <li v-for="tab in tabs" :key="tab.testid">
          <RouterLink
            :to="tab.to"
            :data-testid="tab.testid"
            class="inline-flex h-11 items-center border-b-2 border-transparent text-sm font-medium text-on-surface-muted transition-colors hover:border-border-strong hover:text-on-surface focus-visible:outline-2 focus-visible:outline-focus"
            exact-active-class="!border-primary !text-on-surface"
          >
            {{ t(tab.label) }}
          </RouterLink>
        </li>
      </ul>
    </nav>
    <RouterView />
  </div>
</template>
