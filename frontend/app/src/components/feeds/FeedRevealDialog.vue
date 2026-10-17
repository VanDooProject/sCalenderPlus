<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { KeyRound } from '@lucide/vue'
import { UiAlert, UiButton, UiDialog } from '@scalenderplus/ui'
import FeedAddressReveal from '@/components/feeds/FeedAddressReveal.vue'
import TextLink from '@/components/TextLink.vue'
import type { FeedAddress } from '@/composables/feeds'

/**
 * New secret addresses after a rotation, shown once: one address with its QR code, or after
 * "new addresses for all links" one compact entry per link.
 */
defineProps<{
  title: string
  items: { id: string; label: string; address: FeedAddress }[]
}>()
const open = defineModel<boolean>('open', { default: false })

const { t } = useI18n()
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="title"
    :close-label="t('common.close')"
    size="lg"
    testid="feed-reveal-dialog"
  >
    <FeedAddressReveal
      v-if="items.length === 1"
      :address="items[0]!.address"
      :label="items[0]!.label"
      testid="revealed-feed"
    />
    <div v-else class="flex flex-col gap-5">
      <UiAlert variant="warning">
        <template #icon><KeyRound aria-hidden="true" /></template>
        {{ t('feeds.reveal.once') }}
      </UiAlert>
      <p v-if="items.length === 0" class="text-sm text-on-surface-muted">
        {{ t('feeds.list.empty') }}
      </p>
      <ul class="flex flex-col gap-4">
        <li
          v-for="item in items"
          :key="item.id"
          class="flex flex-col gap-2"
          :data-testid="`revealed-feed-${item.id}`"
        >
          <p class="text-sm font-medium">{{ item.label }}</p>
          <FeedAddressReveal
            :address="item.address"
            :label="item.label"
            :qr="false"
            compact
            :testid="`revealed-feed-${item.id}-address`"
          />
        </li>
      </ul>
      <p class="text-sm">
        <TextLink :to="{ name: 'help-calendar-apps' }">{{ t('feeds.help') }}</TextLink>
      </p>
    </div>
    <template #footer>
      <UiButton data-testid="feed-reveal-done" @click="open = false">
        {{ t('feeds.reveal.done') }}
      </UiButton>
    </template>
  </UiDialog>
</template>
