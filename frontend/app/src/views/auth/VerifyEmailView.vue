<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { CircleCheck, Unlink } from '@lucide/vue'
import { UiButton, UiSpinner, useToast } from '@scalenderplus/ui'
import { api } from '@/api'
import AuthHeading from '@/components/AuthHeading.vue'
import FormAlert from '@/components/FormAlert.vue'
import { sessionQueryKey, useSession } from '@/composables/session'
import { call } from '@/lib/apiError'
import { errorMessage, isApiError } from '@/lib/errorMessages'

const { t } = useI18n()
const route = useRoute()
const queryClient = useQueryClient()
const toast = useToast()
const { user } = useSession()
const state = ref<'pending' | 'done' | 'invalid-link' | 'error'>('pending')
const error = ref<string | null>(null)

async function confirm() {
  const userId = typeof route.query.userId === 'string' ? route.query.userId : ''
  const token = typeof route.query.token === 'string' ? route.query.token : ''
  if (!userId || !token) {
    state.value = 'invalid-link'
    return
  }
  state.value = 'pending'
  try {
    await call(api.POST('/api/v1/auth/confirm-email', { body: { userId, token } }))
    state.value = 'done'
    void queryClient.invalidateQueries({ queryKey: sessionQueryKey })
  } catch (e) {
    if (isApiError(e, 'token_invalid')) {
      state.value = 'invalid-link'
    } else {
      error.value = errorMessage(t, e)
      state.value = 'error'
    }
  }
}

const resend = useMutation({
  mutationFn: () => call(api.POST('/api/v1/auth/confirm-email/resend')),
  onSuccess: () => toast.success(t('verifyBanner.sent')),
})

onMounted(confirm)
</script>

<template>
  <div v-if="state === 'pending'" class="flex flex-col items-center gap-4 py-8 text-center">
    <UiSpinner class="size-8 text-primary" :label="t('auth.verify.pending')" />
    <h1 class="text-lg font-semibold">{{ t('auth.verify.pending') }}</h1>
  </div>

  <div v-else-if="state === 'done'" data-testid="verify-done" aria-live="polite">
    <AuthHeading :title="t('auth.verify.doneTitle')" :description="t('auth.verify.doneText')">
      <template #icon><CircleCheck aria-hidden="true" /></template>
    </AuthHeading>
    <UiButton as-child size="lg" block>
      <RouterLink
        :to="user ? { name: 'calendar' } : { name: 'login' }"
        data-testid="verify-continue"
      >
        {{ user ? t('auth.verify.continue') : t('auth.verify.toLogin') }}
      </RouterLink>
    </UiButton>
  </div>

  <div v-else-if="state === 'invalid-link'" data-testid="verify-invalid">
    <AuthHeading :title="t('auth.linkInvalidTitle')" :description="t('auth.verify.invalidText')">
      <template #icon><Unlink aria-hidden="true" /></template>
    </AuthHeading>
    <UiButton
      v-if="user && !user.emailVerified"
      size="lg"
      block
      :loading="resend.isPending.value"
      @click="resend.mutate()"
    >
      {{ t('verifyBanner.resend') }}
    </UiButton>
    <UiButton v-else as-child size="lg" block variant="secondary">
      <RouterLink :to="user ? { name: 'calendar' } : { name: 'login' }">
        {{ user ? t('auth.verify.continue') : t('auth.verify.toLogin') }}
      </RouterLink>
    </UiButton>
  </div>

  <div v-else class="flex flex-col gap-5">
    <AuthHeading :title="t('auth.verify.errorTitle')" />
    <FormAlert :message="error" />
    <UiButton size="lg" block @click="confirm">{{ t('common.retry') }}</UiButton>
  </div>
</template>
