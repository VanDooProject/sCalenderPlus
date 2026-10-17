<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQueryClient } from '@tanstack/vue-query'
import { Globe, Plus, Trash2 } from '@lucide/vue'
import {
  UiAlert,
  UiButton,
  UiDialog,
  UiField,
  UiInput,
  UiSelect,
  useToast,
} from '@scalenderplus/ui'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import SettingsCard from '@/components/SettingsCard.vue'
import FeedAddressReveal from '@/components/feeds/FeedAddressReveal.vue'
import {
  feedKeys,
  shareLinkExpiryOptions,
  useFeedLabels,
  useShareLinks,
  type IssuedShareLink,
  type ShareLink,
} from '@/composables/feeds'
import { useSession } from '@/composables/session'
import { ApiError, call } from '@/lib/apiError'
import { calendarErrorMessage } from '@/lib/calendarLevels'
import { isApiError } from '@/lib/errorMessages'

/**
 * Anonymous share links of a calendar (`manage`, api.md "Share links"): anyone with the address
 * sees the calendar in a calendar app, busy times only or with details, until it expires or is
 * revoked. The address is shown once after creating.
 */
const props = defineProps<{ calendarId: string; frozen: boolean }>()

const { t } = useI18n()
const toast = useToast()
const queryClient = useQueryClient()
const { user } = useSession()
const links = useShareLinks(() => props.calendarId)
const { formatDate, formatDateTime } = useFeedLabels()
const verified = computed(() => user.value?.emailVerified ?? true)

const refresh = () =>
  queryClient.invalidateQueries({ queryKey: feedKeys.shareLinks(props.calendarId) })

const isExpired = (link: ShareLink) =>
  link.expiresAt !== null && Date.parse(link.expiresAt) <= Date.now()

function linkTitle(link: ShareLink) {
  return link.label ?? t(`shareLinks.levels.${link.level === 'read' ? 'read' : 'free_busy'}.label`)
}

const errorText = (error: unknown) =>
  isApiError(error, 'conflict') ? t('shareLinks.errors.limit') : calendarErrorMessage(t, error)

// Creating.
const createOpen = ref(false)
const level = ref<string>('free_busy')
const label = ref('')
const expiry = ref('0')
const createError = ref<string | null>(null)
const creating = ref(false)
const issued = ref<IssuedShareLink | null>(null)
watch(createOpen, (open) => {
  if (!open) return
  level.value = 'free_busy'
  label.value = ''
  expiry.value = '0'
  createError.value = null
  issued.value = null
})

const levelOptions = computed(() =>
  (['free_busy', 'read'] as const).map((l) => ({
    value: l as string,
    label: t(`shareLinks.levels.${l}.label`),
  })),
)
const expiryOptions = computed(() =>
  shareLinkExpiryOptions.map((days) => ({
    value: String(days),
    label: days === 0 ? t('shareLinks.create.never') : t('shareLinks.create.days', days),
  })),
)

async function create() {
  creating.value = true
  createError.value = null
  try {
    const days = Number(expiry.value)
    const { data } = await call(
      api.POST('/api/v1/calendars/{id}/share-links', {
        params: { path: { id: props.calendarId } },
        body: {
          level: level.value,
          ...(label.value.trim() ? { label: label.value.trim() } : {}),
          ...(days > 0 ? { expiresInDays: days } : {}),
        },
      }),
    )
    issued.value = data
    await refresh()
  } catch (error) {
    createError.value = errorText(error)
  } finally {
    creating.value = false
  }
}

// Revoking.
const revokeTarget = ref<ShareLink | null>(null)
const revokeOpen = ref(false)
const revokeError = ref<string | null>(null)
const revoking = ref(false)
function askRevoke(link: ShareLink) {
  revokeTarget.value = link
  revokeError.value = null
  revokeOpen.value = true
}

async function revoke() {
  const link = revokeTarget.value
  if (!link) return
  revoking.value = true
  revokeError.value = null
  try {
    try {
      await call(api.DELETE('/api/v1/share-links/{id}', { params: { path: { id: link.id } } }))
    } catch (error) {
      if (!(error instanceof ApiError && error.status === 404)) throw error
    }
    await refresh()
    revokeOpen.value = false
    toast.success(t('shareLinks.revoke.done', { name: linkTitle(link) }))
  } catch (error) {
    revokeError.value = errorText(error)
  } finally {
    revoking.value = false
  }
}
</script>

<template>
  <SettingsCard :title="t('shareLinks.title')" :description="t('shareLinks.description')">
    <div class="flex flex-col gap-4" data-testid="share-links">
      <UiAlert v-if="!verified" variant="warning" data-testid="share-links-verify">
        {{ t('shareLinks.verifyFirst') }}
      </UiAlert>
      <p v-if="links.isPending.value" class="text-sm text-on-surface-muted">
        {{ t('common.loading') }}
      </p>
      <FormAlert v-else-if="links.isError.value" :message="errorText(links.error.value)" />
      <p
        v-else-if="(links.data.value ?? []).length === 0"
        class="text-sm text-on-surface-muted"
        data-testid="share-links-empty"
      >
        {{ t('shareLinks.empty') }}
      </p>
      <ul v-else class="flex flex-col divide-y divide-border" data-testid="share-links-list">
        <li
          v-for="link in links.data.value"
          :key="link.id"
          class="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:gap-4"
          :data-testid="`share-link-${link.id}`"
        >
          <span class="flex min-w-0 flex-1 items-start gap-2 text-sm">
            <Globe class="mt-0.5 size-4 shrink-0 text-on-surface-muted" aria-hidden="true" />
            <span class="flex min-w-0 flex-col gap-0.5">
              <span class="font-medium break-words">{{ linkTitle(link) }}</span>
              <span class="text-on-surface-muted">
                {{ t(`shareLinks.levels.${link.level === 'read' ? 'read' : 'free_busy'}.label`) }}
                ·
                {{
                  link.expiresAt
                    ? isExpired(link)
                      ? t('shareLinks.list.expired', { when: formatDate(link.expiresAt) })
                      : t('shareLinks.list.expires', { when: formatDate(link.expiresAt) })
                    : t('shareLinks.list.noExpiry')
                }}
                ·
                {{
                  link.lastUsedAt
                    ? t('feeds.list.lastUsed', { when: formatDateTime(link.lastUsedAt) })
                    : t('feeds.list.neverUsed')
                }}
              </span>
            </span>
          </span>
          <UiButton
            variant="ghost"
            size="sm"
            class="self-start text-on-danger-soft sm:self-auto"
            :aria-label="t('shareLinks.revoke.actionFor', { name: linkTitle(link) })"
            :data-testid="`share-link-revoke-${link.id}`"
            @click="askRevoke(link)"
          >
            <Trash2 class="size-4" aria-hidden="true" />{{ t('shareLinks.revoke.action') }}
          </UiButton>
        </li>
      </ul>
    </div>
    <template #footer>
      <p v-if="frozen" class="mr-auto text-sm text-on-surface-muted">
        {{ t('shareLinks.frozenHint') }}
      </p>
      <UiButton
        variant="secondary"
        :disabled="frozen || !verified"
        data-testid="share-link-create"
        @click="createOpen = true"
      >
        <Plus class="size-4" aria-hidden="true" />{{ t('shareLinks.create.action') }}
      </UiButton>
    </template>
  </SettingsCard>

  <UiDialog
    v-model:open="createOpen"
    :title="issued ? t('shareLinks.create.createdTitle') : t('shareLinks.create.title')"
    :description="issued ? undefined : t('shareLinks.create.description')"
    :close-label="t('common.close')"
    size="lg"
    testid="share-link-dialog"
  >
    <FeedAddressReveal v-if="issued" :address="issued" testid="created-share-link" />
    <form
      v-else
      id="share-link-form"
      data-testid="share-link-form"
      class="flex flex-col gap-5"
      novalidate
      @submit.prevent="create"
    >
      <FormAlert :message="createError" />
      <UiField
        :label="t('shareLinks.create.level')"
        :hint="t(`shareLinks.levels.${level === 'read' ? 'read' : 'free_busy'}.hint`)"
      >
        <UiSelect v-model="level" :options="levelOptions" testid="share-link-level" />
      </UiField>
      <div class="grid gap-5 sm:grid-cols-2">
        <UiField :label="t('shareLinks.create.label')" :hint="t('shareLinks.create.labelHint')">
          <UiInput
            v-model="label"
            maxlength="100"
            autocomplete="off"
            data-testid="share-link-label"
          />
        </UiField>
        <UiField :label="t('shareLinks.create.expiry')">
          <UiSelect v-model="expiry" :options="expiryOptions" testid="share-link-expiry" />
        </UiField>
      </div>
    </form>
    <template #footer>
      <UiButton v-if="issued" data-testid="share-link-done" @click="createOpen = false">
        {{ t('feeds.reveal.done') }}
      </UiButton>
      <template v-else>
        <UiButton variant="ghost" @click="createOpen = false">{{ t('common.cancel') }}</UiButton>
        <UiButton
          type="submit"
          form="share-link-form"
          :loading="creating"
          data-testid="share-link-submit"
        >
          {{ t('shareLinks.create.submit') }}
        </UiButton>
      </template>
    </template>
  </UiDialog>

  <UiDialog
    v-model:open="revokeOpen"
    :title="t('shareLinks.revoke.title', { name: revokeTarget ? linkTitle(revokeTarget) : '' })"
    :description="t('shareLinks.revoke.text')"
    :close-label="t('common.close')"
    testid="share-link-revoke-dialog"
  >
    <div class="flex flex-col gap-4">
      <FormAlert :message="revokeError" />
      <p class="text-sm text-on-surface-muted">{{ t('help.calendarApps.caveats.recall') }}</p>
    </div>
    <template #footer>
      <UiButton variant="ghost" @click="revokeOpen = false">{{ t('common.cancel') }}</UiButton>
      <UiButton
        variant="danger"
        :loading="revoking"
        data-testid="share-link-revoke-submit"
        @click="revoke"
      >
        {{ t('shareLinks.revoke.action') }}
      </UiButton>
    </template>
  </UiDialog>
</template>
