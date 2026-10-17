<script setup lang="ts">
import { nextTick, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { UiToaster } from '@scalenderplus/ui'
import PaywallDialog from '@/components/PaywallDialog.vue'
import UpdatePrompt from '@/components/UpdatePrompt.vue'
import { useBrand } from '@/config'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const brand = useBrand()

// Page title per route and language.
watch(
  [() => route.meta.title, locale],
  ([title]) => {
    const name = brand.name
    document.title = title ? `${t(title)} · ${name}` : name
  },
  { immediate: true },
)

// After client-side navigation, move focus to the new page's heading so screen readers announce it
// (the initial load keeps the browser's default focus).
router.afterEach(async (to, from, failure) => {
  if (failure || from.matched.length === 0 || to.path === from.path) return
  await nextTick()
  const heading = document.querySelector<HTMLElement>('main h1')
  if (heading) {
    heading.tabIndex = -1
    heading.focus({ preventScroll: true })
  }
})
</script>

<template>
  <RouterView />
  <PaywallDialog />
  <UpdatePrompt />
  <UiToaster :label="t('a11y.notifications')" :close-label="t('common.close')" />
</template>
