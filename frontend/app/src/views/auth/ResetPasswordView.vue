<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { CircleCheck, Unlink } from '@lucide/vue'
import { UiButton, UiField } from '@scalenderplus/ui'
import { api } from '@/api'
import AuthHeading from '@/components/AuthHeading.vue'
import FormAlert from '@/components/FormAlert.vue'
import PasswordInput from '@/components/PasswordInput.vue'
import TextLink from '@/components/TextLink.vue'
import { useForm, useValidators } from '@/composables/form'
import { sessionQueryKey } from '@/composables/session'
import { call } from '@/lib/apiError'
import { isApiError } from '@/lib/errorMessages'

const { t } = useI18n()
const route = useRoute()
const queryClient = useQueryClient()
const rules = useValidators()
const formRef = ref<HTMLFormElement>()
const state = ref<'form' | 'done' | 'invalid-link'>('form')

const userId = computed(() => (typeof route.query.userId === 'string' ? route.query.userId : ''))
const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : ''))
if (!userId.value || !token.value) state.value = 'invalid-link'

const form = useForm(
  { newPassword: '', confirm: '' },
  {
    formRef,
    validate: (v) => ({
      newPassword: rules.password(v.newPassword),
      confirm:
        v.confirm === ''
          ? t('validation.required')
          : v.confirm !== v.newPassword
            ? t('validation.passwordMismatch')
            : undefined,
    }),
  },
)

async function submit() {
  await form.submit(async (v) => {
    try {
      await call(
        api.POST('/api/v1/auth/reset-password', {
          body: { userId: userId.value, token: token.value, newPassword: v.newPassword },
        }),
      )
      // A reset ends every session (security stamp), this browser's too.
      queryClient.setQueryData(sessionQueryKey, null)
      state.value = 'done'
    } catch (error) {
      if (isApiError(error, 'token_invalid')) {
        state.value = 'invalid-link'
        return
      }
      throw error
    }
  })
}
</script>

<template>
  <div v-if="state === 'done'" data-testid="reset-done" aria-live="polite">
    <AuthHeading :title="t('auth.reset.doneTitle')" :description="t('auth.reset.doneText')">
      <template #icon><CircleCheck aria-hidden="true" /></template>
    </AuthHeading>
    <UiButton as-child size="lg" block>
      <RouterLink :to="{ name: 'login' }" data-testid="reset-to-login">
        {{ t('auth.reset.toLogin') }}
      </RouterLink>
    </UiButton>
  </div>

  <div v-else-if="state === 'invalid-link'" data-testid="reset-invalid">
    <AuthHeading :title="t('auth.linkInvalidTitle')" :description="t('auth.reset.invalidText')">
      <template #icon><Unlink aria-hidden="true" /></template>
    </AuthHeading>
    <UiButton as-child size="lg" block>
      <RouterLink :to="{ name: 'forgot-password' }">{{ t('auth.reset.requestNew') }}</RouterLink>
    </UiButton>
  </div>

  <div v-else>
    <AuthHeading :title="t('auth.reset.title')" :description="t('auth.reset.description')" />
    <form
      ref="formRef"
      class="flex flex-col gap-5"
      novalidate
      data-testid="reset-form"
      @submit.prevent="submit"
    >
      <FormAlert :message="form.formError.value" />
      <UiField
        :label="t('auth.newPassword')"
        :hint="t('auth.passwordHint', { min: 10 })"
        :errors="form.errors('newPassword')"
        required
      >
        <PasswordInput
          v-model="form.values.newPassword"
          name="newPassword"
          autocomplete="new-password"
          data-testid="reset-password"
        />
      </UiField>
      <UiField :label="t('auth.confirmPassword')" :errors="form.errors('confirm')" required>
        <PasswordInput
          v-model="form.values.confirm"
          name="confirm"
          autocomplete="new-password"
          data-testid="reset-confirm"
        />
      </UiField>
      <UiButton
        type="submit"
        size="lg"
        block
        :loading="form.pending.value"
        data-testid="reset-submit"
      >
        {{ t('auth.reset.submit') }}
      </UiButton>
    </form>
    <p class="mt-6 text-center text-sm">
      <TextLink :to="{ name: 'login' }">{{ t('auth.backToLogin') }}</TextLink>
    </p>
  </div>
</template>
