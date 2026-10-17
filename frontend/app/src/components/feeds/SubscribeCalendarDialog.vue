<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { UiAlert, UiButton, UiDialog, UiSpinner } from '@scalenderplus/ui'
import FormAlert from '@/components/FormAlert.vue'
import TextLink from '@/components/TextLink.vue'
import FeedAddressReveal from '@/components/feeds/FeedAddressReveal.vue'
import {
  useFeedActions,
  useFeedLabels,
  useFeedTokens,
  type IssuedFeedToken,
} from '@/composables/feeds'
import { errorMessage, isApiError } from '@/lib/errorMessages'

/**
 * "Subscribe in calendar app" for one calendar: creates a calendar feed link, or — when there is
 * one already, whose address cannot be shown again — offers a new address for it (the old one stops
 * working) or another link.
 */
const props = defineProps<{ calendarId: string; calendarName: string }>()
const open = defineModel<boolean>('open', { default: false })

const { t } = useI18n()
const tokens = useFeedTokens(open)
const { create, rotate } = useFeedActions()
const { formatDateTime } = useFeedLabels()

const issued = ref<IssuedFeedToken | null>(null)
const busy = ref(false)
const error = ref<string | null>(null)
watch(open, (isOpen) => {
  if (!isOpen) return
  issued.value = null
  error.value = null
})

/** The newest feed link of this calendar, if any. */
const existing = computed(
  () =>
    (tokens.data.value ?? [])
      .filter((t) => t.scope === 'calendar' && t.calendarId === props.calendarId)
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0],
)

async function run(action: () => Promise<IssuedFeedToken>) {
  busy.value = true
  error.value = null
  try {
    issued.value = await action()
  } catch (e) {
    error.value = isApiError(e, 'conflict') ? t('feeds.errors.limit') : errorMessage(t, e)
  } finally {
    busy.value = false
  }
}

const createNew = () => run(() => create({ scope: 'calendar', calendarId: props.calendarId }))
const renew = () => {
  const token = existing.value
  if (token) void run(() => rotate(token.id))
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="t('feeds.subscribe.title', { name: calendarName })"
    :close-label="t('common.close')"
    size="lg"
    testid="subscribe-dialog"
  >
    <FeedAddressReveal v-if="issued" :address="issued" testid="subscribe-feed" />
    <div v-else class="flex flex-col gap-4 text-sm">
      <FormAlert :message="error" />
      <div v-if="tokens.isPending.value" class="flex justify-center py-6">
        <UiSpinner class="size-6 text-primary" :label="t('common.loading')" />
      </div>
      <template v-else-if="existing">
        <UiAlert
          :variant="existing.needsNewLink ? 'warning' : 'info'"
          data-testid="subscribe-existing"
        >
          <p>
            {{
              existing.needsNewLink
                ? t('feeds.subscribe.existingBroken')
                : t('feeds.subscribe.existing', {
                    when: existing.lastUsedAt
                      ? formatDateTime(existing.lastUsedAt)
                      : t('feeds.subscribe.notYet'),
                  })
            }}
          </p>
        </UiAlert>
        <p class="text-on-surface-muted">{{ t('feeds.subscribe.existingChoices') }}</p>
      </template>
      <p v-else class="text-on-surface-muted">
        {{ t('feeds.subscribe.text', { name: calendarName }) }}
      </p>
      <p class="text-on-surface-muted">
        {{ t('feeds.subscribe.aggregateTip') }}
        <TextLink :to="{ name: 'settings-feeds' }">{{ t('feeds.subscribe.allLinks') }}</TextLink>
      </p>
      <p>
        <TextLink :to="{ name: 'help-calendar-apps' }">{{ t('feeds.help') }}</TextLink>
      </p>
    </div>
    <template #footer>
      <UiButton v-if="issued" data-testid="subscribe-done" @click="open = false">
        {{ t('feeds.reveal.done') }}
      </UiButton>
      <template v-else-if="existing">
        <UiButton
          variant="secondary"
          :disabled="busy"
          data-testid="subscribe-another"
          @click="createNew"
        >
          {{ t('feeds.subscribe.another') }}
        </UiButton>
        <UiButton :loading="busy" data-testid="subscribe-renew" @click="renew">
          {{ t('feeds.subscribe.renew') }}
        </UiButton>
      </template>
      <template v-else>
        <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
        <UiButton
          :loading="busy"
          :disabled="tokens.isPending.value"
          data-testid="subscribe-create"
          @click="createNew"
        >
          {{ t('feeds.subscribe.create') }}
        </UiButton>
      </template>
    </template>
  </UiDialog>
</template>
