<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RefreshCw } from '@lucide/vue'
import { UiButton } from '@scalenderplus/ui'
import { applyUpdate, dismissUpdate, updateAvailable } from '@/pwa'

/**
 * "A new version is available" once the service worker has installed a new build (src/pwa.ts):
 * reloading activates it; "Later" keeps the running version until the next visit.
 */
const { t } = useI18n()
const reloading = ref(false)

async function reload() {
  reloading.value = true
  try {
    await applyUpdate()
  } finally {
    reloading.value = false
  }
}
</script>

<template>
  <div
    v-if="updateAvailable"
    class="fixed inset-x-4 bottom-4 z-50 mx-auto flex max-w-md flex-wrap items-center gap-3 rounded-xl border border-border bg-surface-raised p-4 text-sm text-on-surface shadow-overlay sm:flex-nowrap"
    data-testid="update-prompt"
  >
    <RefreshCw class="size-5 shrink-0 text-primary" aria-hidden="true" />
    <p role="status" class="min-w-0 flex-1">{{ t('pwa.updateAvailable') }}</p>
    <div class="flex shrink-0 gap-2">
      <UiButton variant="ghost" size="sm" data-testid="update-later" @click="dismissUpdate">
        {{ t('pwa.later') }}
      </UiButton>
      <UiButton size="sm" :loading="reloading" data-testid="update-reload" @click="reload">
        {{ t('pwa.reload') }}
      </UiButton>
    </div>
  </div>
</template>
