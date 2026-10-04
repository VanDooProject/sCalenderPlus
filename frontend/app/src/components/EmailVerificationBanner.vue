<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useMutation } from '@tanstack/vue-query'
import { MailWarning } from '@lucide/vue'
import { useToast } from '@scalenderplus/ui'
import { api } from '@/api'
import { call } from '@/lib/apiError'

defineProps<{ email: string }>()
const { t } = useI18n()
const toast = useToast()

const resend = useMutation({
  mutationFn: () => call(api.POST('/api/v1/auth/confirm-email/resend')),
  onSuccess: () => toast.success(t('verifyBanner.sent')),
})
</script>

<template>
  <div
    class="flex flex-col gap-2 border-b border-on-warning-soft/15 bg-warning-soft px-4 py-2.5 text-sm text-on-warning-soft sm:flex-row sm:items-center sm:gap-3 lg:px-8"
    data-testid="verify-banner"
  >
    <p class="flex flex-1 items-start gap-2">
      <MailWarning class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <span>{{ t('verifyBanner.text', { email }) }}</span>
    </p>
    <button
      type="button"
      class="self-start rounded-md px-2 py-1 font-semibold underline underline-offset-2 hover:bg-on-warning-soft/10 focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-60 sm:self-auto"
      :disabled="resend.isPending.value"
      @click="resend.mutate()"
    >
      {{ t('verifyBanner.resend') }}
    </button>
  </div>
</template>
