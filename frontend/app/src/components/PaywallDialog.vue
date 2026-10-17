<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Sparkles } from '@lucide/vue'
import { UiButton, UiDialog } from '@scalenderplus/ui'
import { closePaywall, paywallOffer } from '@/composables/paywall'
import { isKnownLimitKey, planKind, suppress, suppressionKey } from '@/lib/planLimits'

/**
 * The contextual upgrade dialog of a `402` (composables/paywall.ts): what the limit is, how much of
 * it is used, whose plan it is (Free, the Pro trial, paid Pro, Team, self-hosted) and what upgrading
 * would change. No checkout during the beta ("coming soon"). "Not now" hides the same dialog for
 * 14 days; closing it otherwise shows it again next time.
 */
const { t, locale } = useI18n()

const offer = computed(() => paywallOffer.value)
const open = computed({
  get: () => offer.value !== null,
  set: (value: boolean) => {
    if (!value) closePaywall()
  },
})

const problem = computed(() => offer.value?.problem ?? null)
const kind = computed(() => (problem.value ? planKind(problem.value) : 'unknown'))
const limitKey = computed(() =>
  problem.value?.kind === 'limit' && isKnownLimitKey(problem.value.key)
    ? problem.value.key
    : 'unknown',
)
const otherOwner = computed(() => offer.value?.owner === 'other')

const title = computed(() =>
  problem.value?.kind === 'feature'
    ? t('paywall.title.feature')
    : t(`paywall.title.${limitKey.value}`),
)

const trialEnd = computed(() =>
  problem.value?.trialEndsAt
    ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'long' }).format(
        new Date(problem.value.trialEndsAt),
      )
    : '',
)

const planSentence = computed(() => {
  const p = problem.value
  if (!p) return ''
  if (p.kind === 'feature') {
    const plan = kind.value === 'trial' ? 'pro' : kind.value
    return plan === 'unknown'
      ? t('paywall.featureUnknownPlan')
      : t('paywall.feature', { plan: t(`paywall.planNames.${plan}`) })
  }
  return t(`paywall.plan.${kind.value}`, { date: trialEnd.value })
})

const benefit = computed(() => {
  if (otherOwner.value) return null
  if (kind.value === 'free') {
    return problem.value?.kind === 'feature'
      ? t('paywall.benefit.feature')
      : t(`paywall.benefit.${limitKey.value}`)
  }
  if (kind.value === 'trial') return t('paywall.benefit.trial', { date: trialEnd.value })
  return null
})

/** Upgrading isn't possible yet: say so instead of a checkout button (beta). */
const comingSoon = computed(
  () => !otherOwner.value && ['free', 'trial', 'pro', 'unknown'].includes(kind.value),
)

const usage = computed(() => {
  const p = problem.value
  if (p?.kind !== 'limit' || p.max === null) return null
  return {
    label: t(`paywall.label.${limitKey.value}`),
    value:
      p.used === null
        ? t('paywall.max', { max: p.max })
        : t('paywall.used', { used: p.used, max: p.max }),
    percent: p.used === null ? 100 : Math.min(100, Math.round((p.used / Math.max(1, p.max)) * 100)),
  }
})

function notNow() {
  if (problem.value) suppress(suppressionKey(problem.value))
  closePaywall()
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="title"
    :close-label="t('common.close')"
    size="md"
    testid="paywall-dialog"
  >
    <div v-if="problem" class="flex flex-col gap-4 text-sm">
      <div
        v-if="usage"
        class="flex flex-col gap-2 rounded-xl border border-border bg-surface-muted p-3"
        data-testid="paywall-usage"
      >
        <div class="flex items-baseline justify-between gap-3">
          <span class="font-medium">{{ usage.label }}</span>
          <span class="font-semibold tabular-nums" data-testid="paywall-numbers">
            {{ usage.value }}
          </span>
        </div>
        <div class="h-1.5 overflow-hidden rounded-full bg-border" aria-hidden="true">
          <!-- Width via CSSOM (allowed by the strict CSP, unlike a style attribute in markup). -->
          <div class="h-full rounded-full bg-primary" :style="{ width: `${usage.percent}%` }" />
        </div>
      </div>
      <p data-testid="paywall-plan">{{ planSentence }}</p>
      <p v-if="benefit" class="font-medium" data-testid="paywall-benefit">{{ benefit }}</p>
      <p v-if="otherOwner" class="text-on-surface-muted" data-testid="paywall-owner-only">
        {{ t('paywall.ownerOnly') }}
      </p>
      <p
        v-if="comingSoon"
        class="flex items-start gap-2 rounded-lg bg-info-soft px-3 py-2 text-on-info-soft"
        data-testid="paywall-coming-soon"
      >
        <Sparkles class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span>{{ t('paywall.comingSoon') }}</span>
      </p>
    </div>
    <template #footer>
      <UiButton variant="ghost" data-testid="paywall-not-now" @click="notNow">
        {{ t('paywall.notNow') }}
      </UiButton>
      <UiButton data-testid="paywall-ok" @click="closePaywall">{{ t('paywall.ok') }}</UiButton>
    </template>
  </UiDialog>
</template>
