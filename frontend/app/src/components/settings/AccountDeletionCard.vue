<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { CalendarDays, Clock, Trash2, Users } from '@lucide/vue'
import { UiAlert, UiButton, UiSpinner, useToast } from '@scalenderplus/ui'
import type { AccountDeletionBlocker, AccountDeletionResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import ReauthDialog, { type ReauthBody } from '@/components/ReauthDialog.vue'
import SettingsCard from '@/components/SettingsCard.vue'
import { sessionQueryKey } from '@/composables/session'
import { call } from '@/lib/apiError'
import { errorMessage, isApiError } from '@/lib/errorMessages'
import { accountDeletionKey, formatPurgeDate } from '@/lib/accountDeletion'

/**
 * Danger zone of the account settings (api.md "Account deletion"): what blocks deleting the account
 * (groups I own alone, calendars I share), the 14-day grace period, the request confirmed with the
 * password or a code (`DELETE /me`), and cancelling a scheduled deletion.
 */
const { t, locale } = useI18n()
const toast = useToast()
const queryClient = useQueryClient()

const state = useQuery({
  queryKey: accountDeletionKey,
  queryFn: async () => (await call(api.GET('/api/v1/me/deletion'))).data,
})
const confirmOpen = ref(false)
const cancelling = ref(false)
const error = ref<string | null>(null)

const scheduled = computed(() => state.data.value?.scheduled ?? null)
const blockers = computed(() => state.data.value?.blockers ?? [])
const graceDays = computed(() => state.data.value?.graceDays ?? 14)

function update(next: AccountDeletionResponse) {
  queryClient.setQueryData(accountDeletionKey, next)
  // `GET /me` carries `deletionScheduledFor` (the banner) and a new ETag.
  void queryClient.invalidateQueries({ queryKey: sessionQueryKey, exact: true })
}

async function requestDeletion(body: ReauthBody) {
  try {
    const { data } = await call(api.DELETE('/api/v1/me', { body }))
    update(data)
    toast.success(t('accountDeletion.scheduledToast'))
  } catch (e) {
    if (isApiError(e, 'account_deletion_blocked')) {
      // Something changed meanwhile (a group got members): show the current blockers.
      confirmOpen.value = false
      await state.refetch()
      error.value = t('accountDeletion.blockedNow')
      return
    }
    throw e
  }
}

async function cancel() {
  cancelling.value = true
  error.value = null
  try {
    const { data } = await call(api.POST('/api/v1/me/deletion/cancel'))
    update(data)
    toast.success(t('accountDeletion.cancelledToast'))
  } catch (e) {
    error.value = errorMessage(t, e)
  } finally {
    cancelling.value = false
  }
}

function blockerLink(blocker: AccountDeletionBlocker) {
  return blocker.type === 'group'
    ? { name: 'group-members', params: { groupId: blocker.id } }
    : { name: 'calendar-settings', params: { calendarId: blocker.id } }
}

function blockerText(blocker: AccountDeletionBlocker) {
  return blocker.type === 'group'
    ? t('accountDeletion.blockers.group', blocker.count)
    : t('accountDeletion.blockers.calendar', blocker.count)
}
</script>

<template>
  <SettingsCard
    :title="t('accountDeletion.title')"
    :description="t('accountDeletion.description', { days: graceDays })"
  >
    <div class="flex flex-col gap-4" data-testid="account-deletion">
      <FormAlert :message="error" />
      <div v-if="state.isPending.value" class="flex justify-center py-4">
        <UiSpinner class="size-6 text-primary" :label="t('common.loading')" />
      </div>
      <FormAlert v-else-if="state.isError.value" :message="errorMessage(t, state.error.value)" />

      <template v-else-if="scheduled">
        <UiAlert variant="danger" data-testid="account-deletion-scheduled">
          <template #icon><Clock aria-hidden="true" /></template>
          {{ t('accountDeletion.scheduled', { date: formatPurgeDate(scheduled.purgeAt, locale) }) }}
        </UiAlert>
        <UiButton
          class="self-start"
          :loading="cancelling"
          data-testid="account-deletion-cancel"
          @click="cancel"
        >
          {{ t('accountDeletion.cancel') }}
        </UiButton>
      </template>

      <template v-else>
        <ul class="flex list-disc flex-col gap-1 pl-5 text-sm text-on-surface-muted">
          <li>{{ t('accountDeletion.effects.personal') }}</li>
          <li>{{ t('accountDeletion.effects.shared') }}</li>
          <li>{{ t('accountDeletion.effects.feeds') }}</li>
          <li>{{ t('accountDeletion.effects.grace', { days: graceDays }) }}</li>
        </ul>
        <div
          v-if="blockers.length"
          class="flex flex-col gap-2"
          data-testid="account-deletion-blockers"
        >
          <UiAlert variant="warning">{{ t('accountDeletion.blockedIntro') }}</UiAlert>
          <ul class="flex flex-col divide-y divide-border rounded-xl border border-border">
            <li
              v-for="blocker in blockers"
              :key="`${blocker.type}-${blocker.id}`"
              class="flex items-start gap-3 px-3 py-2"
              data-testid="account-deletion-blocker"
            >
              <Users
                v-if="blocker.type === 'group'"
                class="mt-0.5 size-4 shrink-0 text-on-surface-muted"
                aria-hidden="true"
              />
              <CalendarDays
                v-else
                class="mt-0.5 size-4 shrink-0 text-on-surface-muted"
                aria-hidden="true"
              />
              <div class="flex min-w-0 flex-col">
                <RouterLink
                  :to="blockerLink(blocker)"
                  class="text-sm font-medium break-words text-primary underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-focus"
                >
                  {{ blocker.name }}
                </RouterLink>
                <span class="text-xs text-on-surface-muted">{{ blockerText(blocker) }}</span>
              </div>
            </li>
          </ul>
        </div>
        <UiButton
          variant="danger"
          class="self-start"
          :disabled="blockers.length > 0"
          data-testid="account-deletion-open"
          @click="confirmOpen = true"
        >
          <Trash2 class="size-4" aria-hidden="true" />{{ t('accountDeletion.delete') }}
        </UiButton>
      </template>
    </div>

    <ReauthDialog
      v-model:open="confirmOpen"
      :title="t('accountDeletion.confirmTitle')"
      :description="t('accountDeletion.confirmText', { days: graceDays })"
      :confirm-label="t('accountDeletion.delete')"
      danger
      testid="account-deletion-dialog"
      :action="requestDeletion"
    />
  </SettingsCard>
</template>
