<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { CalendarSync, Plus, RefreshCw, Rss, Trash2, TriangleAlert } from '@lucide/vue'
import { UiAlert, UiButton, UiDialog, UiSpinner, useToast } from '@scalenderplus/ui'
import EmptyState from '@/components/EmptyState.vue'
import FormAlert from '@/components/FormAlert.vue'
import SettingsCard from '@/components/SettingsCard.vue'
import TextLink from '@/components/TextLink.vue'
import CreateFeedDialog from '@/components/feeds/CreateFeedDialog.vue'
import FeedRevealDialog from '@/components/feeds/FeedRevealDialog.vue'
import { useBrand } from '@/config'
import { useCalendars } from '@/composables/calendars'
import {
  useFeedActions,
  useFeedLabels,
  useFeedTokens,
  type FeedToken,
  type IssuedFeedToken,
} from '@/composables/feeds'
import { errorMessage } from '@/lib/errorMessages'

/**
 * `/settings/feeds`: the user's personal iCal feed links (api.md "Feeds"). Secrets are never
 * listed: an address is shown once when a link is created or gets a new address.
 */
const { t } = useI18n()
const toast = useToast()
const app = useBrand().name
const tokens = useFeedTokens()
const calendars = useCalendars()
const { rotate, rotateAll, revoke } = useFeedActions()
const { scopeLabel, formatDate, formatDateTime } = useFeedLabels()

const calendarName = (id: string | null) =>
  id ? calendars.data.value?.find((c) => c.id === id)?.name : undefined
const whatLabel = (token: FeedToken) => scopeLabel(token, calendarName(token.calendarId))
const title = (token: FeedToken) => token.name ?? whatLabel(token)

const items = computed(() => tokens.data.value ?? [])
const needsNew = computed(() => items.value.filter((t) => t.needsNewLink))

const createOpen = ref(false)

// New addresses, shown once.
const revealOpen = ref(false)
const revealTitle = ref('')
const revealed = ref<{ id: string; label: string; address: IssuedFeedToken }[]>([])
function reveal(heading: string, issued: IssuedFeedToken[]) {
  revealTitle.value = heading
  revealed.value = issued.map((i) => ({
    id: i.feedToken.id,
    label: title(i.feedToken),
    address: i,
  }))
  revealOpen.value = true
}

// Confirmations: replacing an address and revoking break existing subscriptions.
type Pending = { kind: 'rotate' | 'revoke'; token: FeedToken } | { kind: 'rotateAll' }
const pending = ref<Pending | null>(null)
const confirmOpen = ref(false)
const confirmError = ref<string | null>(null)
const busy = ref(false)
watch(confirmOpen, (open) => {
  if (open) confirmError.value = null
})

function ask(next: Pending) {
  pending.value = next
  confirmOpen.value = true
}

/** A link a password reset broke needs no confirmation: its old address is gone already. */
async function renew(token: FeedToken) {
  busy.value = true
  try {
    const issued = await rotate(token.id)
    reveal(t('feeds.reveal.newTitle'), [issued])
  } catch (error) {
    toast.show({ variant: 'error', title: errorMessage(t, error) })
  } finally {
    busy.value = false
  }
}

async function confirm() {
  const p = pending.value
  if (!p) return
  busy.value = true
  confirmError.value = null
  try {
    if (p.kind === 'revoke') {
      await revoke(p.token.id)
      confirmOpen.value = false
      toast.success(t('feeds.revoke.done', { name: title(p.token) }))
    } else if (p.kind === 'rotate') {
      const issued = await rotate(p.token.id)
      confirmOpen.value = false
      reveal(t('feeds.reveal.newTitle'), [issued])
    } else {
      const issued = await rotateAll()
      confirmOpen.value = false
      reveal(t('feeds.rotateAll.revealTitle'), issued)
    }
  } catch (error) {
    confirmError.value = errorMessage(t, error)
  } finally {
    busy.value = false
  }
}

const confirmTitle = computed(() => {
  const p = pending.value
  if (!p) return ''
  if (p.kind === 'rotateAll') return t('feeds.rotateAll.confirmTitle')
  return p.kind === 'revoke'
    ? t('feeds.revoke.title', { name: title(p.token) })
    : t('feeds.rotate.title', { name: title(p.token) })
})
const confirmText = computed(() => {
  const p = pending.value
  if (!p) return ''
  if (p.kind === 'rotateAll') return t('feeds.rotateAll.confirmText')
  return p.kind === 'revoke' ? t('feeds.revoke.text') : t('feeds.rotate.text')
})
</script>

<template>
  <div class="flex flex-col gap-6" data-testid="feeds-page">
    <SettingsCard :title="t('feeds.title')" :description="t('feeds.description', { app })">
      <div class="flex flex-col gap-4">
        <UiAlert
          v-if="needsNew.length > 0"
          variant="warning"
          role="status"
          data-testid="feeds-needs-new"
        >
          <template #icon><TriangleAlert aria-hidden="true" /></template>
          <p class="font-medium">{{ t('feeds.needsNew.title', needsNew.length) }}</p>
          <p>{{ t('feeds.needsNew.text') }}</p>
        </UiAlert>

        <div v-if="tokens.isPending.value" class="flex justify-center py-6">
          <UiSpinner class="size-6 text-primary" :label="t('common.loading')" />
        </div>
        <div v-else-if="tokens.isError.value" class="flex flex-col items-start gap-2">
          <FormAlert :message="errorMessage(t, tokens.error.value)" />
          <UiButton variant="secondary" size="sm" @click="tokens.refetch()">
            {{ t('common.retry') }}
          </UiButton>
        </div>
        <EmptyState
          v-else-if="items.length === 0"
          :title="t('feeds.list.emptyTitle')"
          :description="t('feeds.list.emptyText')"
          data-testid="feeds-empty"
        >
          <template #icon><Rss aria-hidden="true" /></template>
        </EmptyState>
        <ul v-else class="flex flex-col divide-y divide-border" data-testid="feeds-list">
          <li
            v-for="token in items"
            :key="token.id"
            class="flex flex-col gap-3 py-4 first:pt-0 last:pb-0 sm:flex-row sm:items-start sm:gap-4"
            :data-testid="`feed-${token.id}`"
          >
            <div class="flex min-w-0 flex-1 flex-col gap-1 text-sm">
              <p class="flex flex-wrap items-center gap-2">
                <span class="font-medium break-words">{{ title(token) }}</span>
                <span
                  v-if="token.needsNewLink"
                  class="rounded-full bg-warning-soft px-2 py-0.5 text-xs font-medium text-on-warning-soft"
                  data-testid="feed-needs-new"
                >
                  {{ t('feeds.needsNew.badge') }}
                </span>
              </p>
              <p v-if="token.name" class="text-on-surface-muted">{{ whatLabel(token) }}</p>
              <p class="text-on-surface-muted">
                {{ t(`feeds.labelModes.${token.labelMode}.label`) }} ·
                {{ t('feeds.list.pastDays', token.includePastDays) }}
              </p>
              <p class="text-on-surface-muted" :data-testid="`feed-used-${token.id}`">
                {{
                  token.lastUsedAt
                    ? t('feeds.list.lastUsed', { when: formatDateTime(token.lastUsedAt) })
                    : t('feeds.list.neverUsed')
                }}
                · {{ t('feeds.list.created', { when: formatDate(token.createdAt) }) }}
              </p>
            </div>
            <div class="flex flex-wrap gap-2">
              <UiButton
                v-if="token.needsNewLink"
                size="sm"
                :loading="busy"
                :data-testid="`feed-renew-${token.id}`"
                @click="renew(token)"
              >
                <RefreshCw class="size-4" aria-hidden="true" />{{ t('feeds.needsNew.action') }}
              </UiButton>
              <UiButton
                v-else
                variant="secondary"
                size="sm"
                :aria-label="t('feeds.rotate.actionFor', { name: title(token) })"
                :data-testid="`feed-rotate-${token.id}`"
                @click="ask({ kind: 'rotate', token })"
              >
                <RefreshCw class="size-4" aria-hidden="true" />{{ t('feeds.rotate.action') }}
              </UiButton>
              <UiButton
                variant="ghost"
                size="sm"
                class="text-on-danger-soft"
                :aria-label="t('feeds.revoke.actionFor', { name: title(token) })"
                :data-testid="`feed-revoke-${token.id}`"
                @click="ask({ kind: 'revoke', token })"
              >
                <Trash2 class="size-4" aria-hidden="true" />{{ t('feeds.revoke.action') }}
              </UiButton>
            </div>
          </li>
        </ul>
        <p class="text-sm text-on-surface-muted">
          {{ t('feeds.latency') }}
          <TextLink :to="{ name: 'help-calendar-apps' }">{{ t('feeds.help') }}</TextLink>
        </p>
      </div>
      <template #footer>
        <UiButton data-testid="feed-create" @click="createOpen = true">
          <Plus class="size-4" aria-hidden="true" />{{ t('feeds.create.action') }}
        </UiButton>
      </template>
    </SettingsCard>

    <SettingsCard
      v-if="items.length > 0"
      :title="t('feeds.rotateAll.title')"
      :description="t('feeds.rotateAll.description')"
    >
      <UiButton
        variant="secondary"
        data-testid="feed-rotate-all"
        @click="ask({ kind: 'rotateAll' })"
      >
        <CalendarSync class="size-4" aria-hidden="true" />{{ t('feeds.rotateAll.action') }}
      </UiButton>
    </SettingsCard>

    <CreateFeedDialog v-model:open="createOpen" />
    <FeedRevealDialog v-model:open="revealOpen" :title="revealTitle" :items="revealed" />

    <UiDialog
      v-model:open="confirmOpen"
      :title="confirmTitle"
      :description="confirmText"
      :close-label="t('common.close')"
      testid="feed-confirm-dialog"
    >
      <div class="flex flex-col gap-4">
        <FormAlert :message="confirmError" />
        <p class="text-sm text-on-surface-muted">
          {{ t('help.calendarApps.caveats.recall') }}
        </p>
      </div>
      <template #footer>
        <UiButton variant="ghost" @click="confirmOpen = false">{{ t('common.cancel') }}</UiButton>
        <UiButton
          :variant="pending?.kind === 'revoke' ? 'danger' : 'primary'"
          :loading="busy"
          data-testid="feed-confirm-submit"
          @click="confirm"
        >
          {{
            pending?.kind === 'revoke'
              ? t('feeds.revoke.action')
              : pending?.kind === 'rotateAll'
                ? t('feeds.rotateAll.action')
                : t('feeds.rotate.action')
          }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>
