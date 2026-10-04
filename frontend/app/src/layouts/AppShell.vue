<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Menu, CloudOff } from '@lucide/vue'
import { UiButton, UiSheet, UiSpinner } from '@scalenderplus/ui'
import AppSidebar from '@/components/AppSidebar.vue'
import BrandMark from '@/components/BrandMark.vue'
import EmailVerificationBanner from '@/components/EmailVerificationBanner.vue'
import EmptyState from '@/components/EmptyState.vue'
import EnvironmentBadge from '@/components/EnvironmentBadge.vue'
import LocaleMenu from '@/components/LocaleMenu.vue'
import SkipLink from '@/components/SkipLink.vue'
import ThemeMenu from '@/components/ThemeMenu.vue'
import UserMenu from '@/components/UserMenu.vue'
import { useSession } from '@/composables/session'

const { t } = useI18n()
const { query, user } = useSession()
const drawerOpen = ref(false)
</script>

<template>
  <SkipLink />
  <div class="flex min-h-dvh">
    <aside
      class="sticky top-0 hidden h-dvh w-64 shrink-0 border-r border-border bg-surface-muted lg:block"
      data-testid="sidebar"
    >
      <AppSidebar />
    </aside>
    <UiSheet
      v-model:open="drawerOpen"
      :title="t('nav.main')"
      :close-label="t('nav.closeMenu')"
      testid="nav-drawer"
    >
      <AppSidebar @navigate="drawerOpen = false" />
    </UiSheet>

    <div class="flex min-w-0 flex-1 flex-col">
      <header
        class="sticky top-0 z-30 flex h-16 shrink-0 items-center gap-2 border-b border-border bg-surface/85 px-3 backdrop-blur-md sm:px-4 lg:px-8"
      >
        <UiButton
          variant="ghost"
          size="icon"
          class="lg:hidden"
          :aria-label="t('nav.openMenu')"
          :aria-expanded="drawerOpen"
          data-testid="nav-drawer-toggle"
          @click="drawerOpen = true"
        >
          <Menu class="size-5" aria-hidden="true" />
        </UiButton>
        <RouterLink
          :to="{ name: 'calendar' }"
          class="rounded-lg focus-visible:outline-2 focus-visible:outline-focus lg:hidden"
          :aria-label="t('app.name')"
        >
          <BrandMark :show-name="false" />
        </RouterLink>
        <div class="ml-auto flex items-center gap-1 sm:gap-2">
          <EnvironmentBadge />
          <LocaleMenu />
          <ThemeMenu />
          <span class="mx-1 hidden h-6 w-px bg-border sm:block" aria-hidden="true" />
          <UserMenu v-if="user" :user="user" />
        </div>
      </header>

      <EmailVerificationBanner v-if="user && !user.emailVerified" :email="user.email" />

      <main id="main" class="flex-1 px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
        <div v-if="query.isPending.value" class="flex justify-center py-24">
          <UiSpinner class="size-8 text-primary" :label="t('common.loading')" />
        </div>
        <EmptyState
          v-else-if="query.isError.value"
          :title="t('errors.apiUnavailableTitle')"
          :description="t('errors.apiUnavailable')"
          data-testid="api-unavailable"
        >
          <template #icon><CloudOff aria-hidden="true" /></template>
          <UiButton variant="secondary" :loading="query.isFetching.value" @click="query.refetch()">
            {{ t('common.retry') }}
          </UiButton>
        </EmptyState>
        <RouterView v-else />
      </main>
    </div>
  </div>
</template>
