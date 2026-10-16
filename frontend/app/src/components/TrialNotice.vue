<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Hourglass, X } from '@lucide/vue'
import { useEntitlements, useTrialDaysLeft } from '@/composables/entitlements'

/**
 * "Pro trial: N days left" while the reverse trial runs (docs/product/plans.md); hidden without a
 * trial and on self-hosted instances. Dismissed for the browser session (per trial end, so a new
 * trial shows again); no checkout here.
 */
const { t, locale } = useI18n()
const { entitlements } = useEntitlements()

const storageKey = 'scal.trialNotice.dismissed'
function readDismissed(): string | null {
  try {
    return sessionStorage.getItem(storageKey)
  } catch {
    return null
  }
}
const dismissed = ref(readDismissed())

const trialEndsAt = computed(() => entitlements.value?.trialEndsAt ?? null)
const daysLeft = useTrialDaysLeft(entitlements)
const visible = computed(() => daysLeft.value !== null && dismissed.value !== trialEndsAt.value)
const until = computed(() =>
  trialEndsAt.value
    ? t('trial.until', {
        date: new Intl.DateTimeFormat(locale.value, { dateStyle: 'long' }).format(
          new Date(trialEndsAt.value),
        ),
      })
    : '',
)

function dismiss() {
  dismissed.value = trialEndsAt.value
  try {
    if (trialEndsAt.value) sessionStorage.setItem(storageKey, trialEndsAt.value)
  } catch {
    // Storage blocked: hidden until the next reload.
  }
}
</script>

<template>
  <div
    v-if="visible && daysLeft !== null"
    class="flex min-w-0 items-center gap-0.5 rounded-full border border-on-info-soft/20 bg-info-soft py-0.5 pr-0.5 pl-2.5 text-xs font-medium text-on-info-soft"
    data-testid="trial-notice"
  >
    <Hourglass class="mr-1 size-3.5 shrink-0" aria-hidden="true" />
    <p role="status" class="truncate" :title="until">
      {{ t('trial.daysLeft', daysLeft) }}<span class="sr-only">. {{ until }}</span>
    </p>
    <button
      type="button"
      class="grid size-6 shrink-0 place-items-center rounded-full hover:bg-on-info-soft/10 focus-visible:outline-2 focus-visible:outline-focus"
      :aria-label="t('trial.dismiss')"
      data-testid="trial-notice-dismiss"
      @click="dismiss"
    >
      <X class="size-3.5" aria-hidden="true" />
    </button>
  </div>
</template>
