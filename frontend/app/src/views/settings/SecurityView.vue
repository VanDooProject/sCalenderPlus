<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Check, Copy, ShieldCheck, ShieldOff } from '@lucide/vue'
import {
  UiAlert,
  UiButton,
  UiDialog,
  UiField,
  UiSpinner,
  UiInput,
  useToast,
} from '@scalenderplus/ui'
import type { TwoFactorStatus } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import PasswordInput from '@/components/PasswordInput.vue'
import QrCode from '@/components/QrCode.vue'
import ReauthDialog, { type ReauthBody } from '@/components/ReauthDialog.vue'
import RecoveryCodes from '@/components/RecoveryCodes.vue'
import SettingsCard from '@/components/SettingsCard.vue'
import { useForm, useValidators } from '@/composables/form'
import { sessionQueryKey, useSession } from '@/composables/session'
import { call } from '@/lib/apiError'
import { errorMessage } from '@/lib/errorMessages'

const twoFactorKey = ['me', 'two-factor'] as const

const { t } = useI18n()
const toast = useToast()
const queryClient = useQueryClient()
const rules = useValidators()
const { user } = useSession()

const status = useQuery({
  queryKey: twoFactorKey,
  queryFn: async () => (await call(api.GET('/api/v1/me/two-factor'))).data,
})

/** Setup in progress: the new secret (not active until enabled with a code). */
const setup = useMutation({
  meta: { handlesErrors: true },
  mutationFn: async () => (await call(api.POST('/api/v1/me/two-factor/setup'))).data,
})
const copiedKey = ref(false)
/** Freshly issued recovery codes, shown once in a dialog. */
const newCodes = ref<string[] | null>(null)
const codesOpen = computed({
  get: () => newCodes.value !== null,
  set: (open: boolean) => {
    if (!open) newCodes.value = null
  },
})
const disableOpen = ref(false)
const regenerateOpen = ref(false)

const groupedKey = computed(() => setup.data.value?.sharedKey.match(/.{1,4}/g)?.join(' ') ?? '')

async function copyKey() {
  if (!setup.data.value) return
  try {
    await navigator.clipboard.writeText(setup.data.value.sharedKey)
    copiedKey.value = true
    setTimeout(() => (copiedKey.value = false), 2000)
  } catch {
    // Clipboard unavailable (permissions): the key stays visible for manual entry.
  }
}

const enableFormRef = ref<HTMLFormElement>()
const enable = useForm(
  { code: '', password: '' },
  {
    formRef: enableFormRef,
    validate: (v) => ({ code: rules.code(v.code), password: rules.required(v.password) }),
  },
)

function updateStatus(next: TwoFactorStatus) {
  queryClient.setQueryData(twoFactorKey, next)
  // The profile shows `twoFactorEnabled`; the session's ETag changed too.
  void queryClient.invalidateQueries({ queryKey: sessionQueryKey, exact: true })
}

async function submitEnable() {
  await enable.submit(async (v) => {
    const { data } = await call(
      api.POST('/api/v1/me/two-factor/enable', {
        body: { code: v.code.replace(/\s/g, ''), password: v.password },
      }),
    )
    updateStatus({ enabled: true, recoveryCodesLeft: data.recoveryCodes.length })
    setup.reset()
    enable.reset()
    newCodes.value = data.recoveryCodes
    toast.success(t('settings.security.enabled'))
  })
}

function cancelSetup() {
  setup.reset()
  enable.reset()
}

async function disable(body: ReauthBody) {
  await call(api.POST('/api/v1/me/two-factor/disable', { body }))
  updateStatus({ enabled: false, recoveryCodesLeft: 0 })
  toast.success(t('settings.security.disabled'))
}

async function regenerate(body: ReauthBody) {
  const { data } = await call(api.POST('/api/v1/me/two-factor/recovery-codes', { body }))
  updateStatus({ enabled: true, recoveryCodesLeft: data.recoveryCodes.length })
  newCodes.value = data.recoveryCodes
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <SettingsCard
      :title="t('settings.security.twoFactorTitle')"
      :description="t('settings.security.twoFactorDescription')"
    >
      <div v-if="status.isPending.value" class="flex justify-center py-6">
        <UiSpinner class="size-6 text-primary" :label="t('common.loading')" />
      </div>
      <FormAlert v-else-if="status.isError.value" :message="errorMessage(t, status.error.value)" />

      <!-- Enabled -->
      <div
        v-else-if="status.data.value?.enabled"
        class="flex flex-col gap-5"
        data-testid="two-factor-enabled"
      >
        <div class="flex items-start gap-3">
          <span
            class="flex size-10 shrink-0 items-center justify-center rounded-xl bg-success-soft text-on-success-soft"
          >
            <ShieldCheck class="size-5" aria-hidden="true" />
          </span>
          <div class="flex flex-col gap-0.5">
            <p class="font-medium">{{ t('settings.security.statusOn') }}</p>
            <p class="text-sm text-on-surface-muted" data-testid="recovery-left">
              {{ t('settings.security.recoveryLeft', status.data.value.recoveryCodesLeft) }}
            </p>
          </div>
        </div>
        <UiAlert v-if="status.data.value.recoveryCodesLeft <= 2" variant="warning">
          {{ t('settings.security.recoveryLow') }}
        </UiAlert>
        <div class="flex flex-wrap gap-2">
          <UiButton
            variant="secondary"
            data-testid="regenerate-codes"
            @click="regenerateOpen = true"
          >
            {{ t('settings.security.regenerate') }}
          </UiButton>
          <UiButton
            variant="ghost"
            class="text-on-danger-soft"
            data-testid="disable-2fa"
            @click="disableOpen = true"
          >
            {{ t('settings.security.disable') }}
          </UiButton>
        </div>
      </div>

      <!-- Setting up -->
      <div v-else-if="setup.data.value" class="flex flex-col gap-6" data-testid="two-factor-setup">
        <ol class="flex flex-col gap-6">
          <li class="flex flex-col gap-3">
            <p class="text-sm font-medium">{{ t('settings.security.step1') }}</p>
            <div class="flex flex-col items-start gap-4 sm:flex-row">
              <QrCode
                :value="setup.data.value.authenticatorUri"
                :label="t('settings.security.qrLabel')"
                class="size-44 shrink-0 border border-border p-1"
              />
              <div class="flex min-w-0 flex-col gap-2 text-sm">
                <p class="text-on-surface-muted">{{ t('settings.security.manualKey') }}</p>
                <code
                  class="rounded-lg border border-border bg-surface-muted px-3 py-2 font-mono text-sm break-all"
                  data-testid="shared-key"
                >
                  {{ groupedKey }}
                </code>
                <UiButton variant="secondary" size="sm" class="self-start" @click="copyKey">
                  <Check v-if="copiedKey" class="size-4" aria-hidden="true" />
                  <Copy v-else class="size-4" aria-hidden="true" />
                  {{ copiedKey ? t('common.copied') : t('settings.security.copyKey') }}
                </UiButton>
              </div>
            </div>
          </li>
          <li>
            <p class="mb-3 text-sm font-medium">{{ t('settings.security.step2') }}</p>
            <form
              ref="enableFormRef"
              class="flex flex-col gap-4"
              novalidate
              data-testid="enable-form"
              @submit.prevent="submitEnable"
            >
              <FormAlert :message="enable.formError.value" />
              <div class="grid gap-4 sm:grid-cols-2">
                <UiField :label="t('auth.twoFactor.code')" :errors="enable.errors('code')" required>
                  <UiInput
                    v-model="enable.values.code"
                    inputmode="numeric"
                    autocomplete="one-time-code"
                    maxlength="7"
                    class="font-mono tracking-[0.3em]"
                    data-testid="enable-code"
                  />
                </UiField>
                <UiField
                  :label="t('auth.currentPassword')"
                  :errors="enable.errors('password')"
                  required
                >
                  <PasswordInput
                    v-model="enable.values.password"
                    autocomplete="current-password"
                    data-testid="enable-password"
                  />
                </UiField>
              </div>
              <div class="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
                <UiButton variant="ghost" @click="cancelSetup">{{ t('common.cancel') }}</UiButton>
                <UiButton type="submit" :loading="enable.pending.value" data-testid="enable-submit">
                  {{ t('settings.security.enable') }}
                </UiButton>
              </div>
            </form>
          </li>
        </ol>
      </div>

      <!-- Off -->
      <div v-else class="flex flex-col gap-5" data-testid="two-factor-disabled">
        <div class="flex items-start gap-3">
          <span
            class="flex size-10 shrink-0 items-center justify-center rounded-xl bg-surface-hover text-on-surface-muted"
          >
            <ShieldOff class="size-5" aria-hidden="true" />
          </span>
          <div class="flex flex-col gap-0.5">
            <p class="font-medium">{{ t('settings.security.statusOff') }}</p>
            <p class="text-sm text-on-surface-muted">{{ t('settings.security.offText') }}</p>
          </div>
        </div>
        <UiAlert v-if="user && !user.emailVerified" variant="warning">
          {{ t('settings.security.verifyFirst') }}
        </UiAlert>
        <FormAlert :message="setup.isError.value ? errorMessage(t, setup.error.value) : null" />
        <UiButton
          class="self-start"
          :disabled="!!user && !user.emailVerified"
          :loading="setup.isPending.value"
          data-testid="setup-2fa"
          @click="setup.mutate()"
        >
          {{ t('settings.security.setup') }}
        </UiButton>
      </div>
    </SettingsCard>

    <ReauthDialog
      v-model:open="disableOpen"
      :title="t('settings.security.disableTitle')"
      :description="t('settings.security.disableText')"
      :confirm-label="t('settings.security.disable')"
      danger
      testid="disable-dialog"
      :action="disable"
    />
    <ReauthDialog
      v-model:open="regenerateOpen"
      :title="t('settings.security.regenerateTitle')"
      :description="t('settings.security.regenerateText')"
      :confirm-label="t('settings.security.regenerate')"
      testid="regenerate-dialog"
      :action="regenerate"
    />
    <UiDialog
      v-model:open="codesOpen"
      :title="t('settings.security.codesTitle')"
      :description="t('settings.security.codesText')"
      :close-label="t('common.close')"
      testid="codes-dialog"
    >
      <RecoveryCodes v-if="newCodes" :codes="newCodes" />
      <template #footer>
        <UiButton data-testid="codes-done" @click="codesOpen = false">
          {{ t('settings.security.codesSaved') }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>
