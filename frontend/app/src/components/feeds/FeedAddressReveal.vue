<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Check, Copy, ExternalLink, KeyRound } from '@lucide/vue'
import { UiAlert, UiButton, UiField, UiInput } from '@scalenderplus/ui'
import QrCode from '@/components/QrCode.vue'
import TextLink from '@/components/TextLink.vue'
import type { FeedAddress } from '@/composables/feeds'

/**
 * A feed's secret address, shown once after creating or rotating it: the https URL with a copy
 * button, the `webcal://` link that opens a calendar app, and a QR code for phones (it encodes the
 * webcal link, which phone cameras hand to the calendar app). The QR code is drawn locally.
 */
const props = withDefaults(
  defineProps<{
    address: FeedAddress
    /** Accessible name of the address field (e.g. which feed it is). */
    label?: string
    qr?: boolean
    /** Hide the "shown once" notice (a list of several addresses shows it once above). */
    compact?: boolean
    testid?: string
  }>(),
  { qr: true, compact: false, testid: 'feed-address' },
)

const { t } = useI18n()
const copied = ref(false)
const copyFailed = ref(false)

async function copy() {
  copyFailed.value = false
  try {
    await navigator.clipboard.writeText(props.address.url)
    copied.value = true
    setTimeout(() => (copied.value = false), 2000)
  } catch {
    // Clipboard unavailable (permissions): the address stays selectable in the field.
    copyFailed.value = true
  }
}
</script>

<template>
  <div class="flex flex-col gap-4" :data-testid="testid">
    <UiAlert v-if="!compact" variant="warning">
      <template #icon><KeyRound aria-hidden="true" /></template>
      {{ t('feeds.reveal.once') }}
    </UiAlert>
    <div class="flex flex-col gap-4 sm:flex-row sm:items-start">
      <div class="flex min-w-0 flex-1 flex-col gap-3">
        <div class="flex items-end gap-2">
          <UiField
            :label="label ?? t('feeds.reveal.address')"
            :hide-label="compact"
            :hint="copyFailed ? t('feeds.reveal.copyFailed') : undefined"
            class="min-w-0 flex-1"
          >
            <UiInput
              :model-value="address.url"
              readonly
              class="font-mono text-xs"
              :data-testid="`${testid}-url`"
              @focus="($event.target as HTMLInputElement).select()"
            />
          </UiField>
          <UiButton variant="secondary" :data-testid="`${testid}-copy`" @click="copy">
            <Check v-if="copied" class="size-4" aria-hidden="true" />
            <Copy v-else class="size-4" aria-hidden="true" />
            {{ copied ? t('common.copied') : t('common.copy') }}
          </UiButton>
        </div>
        <div class="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
          <UiButton as-child variant="secondary" size="sm">
            <a :href="address.webcalUrl" :data-testid="`${testid}-webcal`">
              <ExternalLink class="size-4" aria-hidden="true" />{{ t('feeds.reveal.openApp') }}
            </a>
          </UiButton>
          <TextLink v-if="!compact" :to="{ name: 'help-calendar-apps' }">
            {{ t('feeds.help') }}
          </TextLink>
        </div>
      </div>
      <figure v-if="qr" class="flex shrink-0 flex-col items-center gap-1">
        <QrCode
          :value="address.webcalUrl"
          :label="t('feeds.reveal.qrLabel')"
          class="size-36 border border-border p-1"
        />
        <figcaption class="max-w-36 text-center text-xs text-on-surface-muted">
          {{ t('feeds.reveal.qrHint') }}
        </figcaption>
      </figure>
    </div>
  </div>
</template>
