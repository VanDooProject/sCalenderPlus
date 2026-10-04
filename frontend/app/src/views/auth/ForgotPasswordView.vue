<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { MailCheck } from '@lucide/vue'
import { UiButton, UiField, UiInput } from '@scalenderplus/ui'
import { api } from '@/api'
import AuthHeading from '@/components/AuthHeading.vue'
import FormAlert from '@/components/FormAlert.vue'
import TextLink from '@/components/TextLink.vue'
import { useForm, useValidators } from '@/composables/form'
import { call } from '@/lib/apiError'

const { t } = useI18n()
const rules = useValidators()
const formRef = ref<HTMLFormElement>()
const sentTo = ref<string | null>(null)

const form = useForm({ email: '' }, { formRef, validate: (v) => ({ email: rules.email(v.email) }) })

async function submit() {
  await form.submit(async (v) => {
    // Always 202, whether or not the address has an account.
    await call(api.POST('/api/v1/auth/forgot-password', { body: { email: v.email.trim() } }))
    sentTo.value = v.email.trim()
  })
}
</script>

<template>
  <div v-if="sentTo" data-testid="forgot-done" aria-live="polite">
    <AuthHeading :title="t('auth.forgot.doneTitle')">
      <template #icon><MailCheck aria-hidden="true" /></template>
    </AuthHeading>
    <i18n-t
      keypath="auth.forgot.doneText"
      tag="p"
      scope="global"
      class="text-sm leading-relaxed text-on-surface-muted"
    >
      <template #email>
        <strong class="font-semibold break-all text-on-surface">{{ sentTo }}</strong>
      </template>
    </i18n-t>
    <UiButton as-child variant="secondary" size="lg" block class="mt-6">
      <RouterLink :to="{ name: 'login' }">{{ t('auth.backToLogin') }}</RouterLink>
    </UiButton>
  </div>

  <div v-else>
    <AuthHeading :title="t('auth.forgot.title')" :description="t('auth.forgot.description')" />
    <form
      ref="formRef"
      class="flex flex-col gap-5"
      novalidate
      data-testid="forgot-form"
      @submit.prevent="submit"
    >
      <FormAlert :message="form.formError.value" />
      <UiField :label="t('auth.email')" :errors="form.errors('email')" required>
        <UiInput
          v-model="form.values.email"
          type="email"
          name="email"
          autocomplete="email"
          inputmode="email"
          data-testid="forgot-email"
        />
      </UiField>
      <UiButton
        type="submit"
        size="lg"
        block
        :loading="form.pending.value"
        data-testid="forgot-submit"
      >
        {{ t('auth.forgot.submit') }}
      </UiButton>
    </form>
    <p class="mt-6 text-center text-sm">
      <TextLink :to="{ name: 'login' }">{{ t('auth.backToLogin') }}</TextLink>
    </p>
  </div>
</template>
