<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { LogOut, ShieldCheck, UserRound } from '@lucide/vue'
import { UiMenu, UiMenuItem, UiMenuSeparator, useToast } from '@scalenderplus/ui'
import type { MeResponse } from '@scalenderplus/api-client'
import { useLogout } from '@/composables/session'
import UserAvatar from './UserAvatar.vue'

defineProps<{ user: MeResponse }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const logout = useLogout()

async function signOut() {
  await logout.mutateAsync()
  await router.replace({ name: 'login' })
  toast.success(t('auth.logout.done'))
}
</script>

<template>
  <UiMenu testid="user-menu">
    <template #trigger>
      <button
        type="button"
        class="flex items-center gap-2 rounded-full p-1 transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus sm:rounded-lg sm:pr-2.5"
        :aria-label="t('nav.userMenu', { name: user.displayName })"
        data-testid="user-menu-trigger"
      >
        <UserAvatar :name="user.displayName" />
        <span class="hidden max-w-36 truncate text-sm font-medium sm:block">
          {{ user.displayName }}
        </span>
      </button>
    </template>
    <div class="flex items-center gap-3 px-2.5 py-2">
      <UserAvatar :name="user.displayName" />
      <div class="min-w-0">
        <p class="truncate text-sm font-semibold">{{ user.displayName }}</p>
        <p class="truncate text-xs text-on-surface-muted" data-testid="user-menu-email">
          {{ user.email }}
        </p>
      </div>
    </div>
    <UiMenuSeparator />
    <UiMenuItem as-child>
      <RouterLink :to="{ name: 'settings-profile' }">
        <UserRound aria-hidden="true" />{{ t('settings.profile.title') }}
      </RouterLink>
    </UiMenuItem>
    <UiMenuItem as-child>
      <RouterLink :to="{ name: 'settings-security' }">
        <ShieldCheck aria-hidden="true" />{{ t('settings.security.title') }}
      </RouterLink>
    </UiMenuItem>
    <UiMenuSeparator />
    <UiMenuItem data-testid="logout" @select="signOut">
      <LogOut aria-hidden="true" />{{ t('auth.logout.action') }}
    </UiMenuItem>
  </UiMenu>
</template>
