<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { MailCheck } from '@lucide/vue'
import { UiButton, UiField, UiInput } from '@scalenderplus/ui'
import { api } from '@/api'
import AuthHeading from '@/components/AuthHeading.vue'
import FormAlert from '@/components/FormAlert.vue'
import PasswordInput from '@/components/PasswordInput.vue'
import TextLink from '@/components/TextLink.vue'
import { useForm, useValidators } from '@/composables/form'
import { call } from '@/lib/apiError'
import { safeNext } from '@/lib/redirect'
import { browserTimeZone } from '@/lib/timeZones'

const { t, locale } = useI18n()
const route = useRoute()
const rules = useValidators()
const formRef = ref<HTMLFormElement>()
/** The address the confirmation went to; set once the api accepted the registration. */
const sentTo = ref<string | null>(null)

const next = computed(() => safeNext(route.query.next))
const loginLink = computed(() => ({ name: 'login', query: next.value ? { next: next.value } : {} }))

const form = useForm(
  { displayName: '', email: '', password: '' },
  {
    formRef,
    validate: (v) => ({
      displayName: rules.required(v.displayName),
      email: rules.email(v.email),
      password: rules.password(v.password),
    }),
  },
)

async function submit() {
  await form.submit(async (v) => {
    // Always 202 (no account enumeration) and never signs in: the user confirms, then logs in.
    await call(
      api.POST('/api/v1/auth/register', {
        body: {
          displayName: v.displayName.trim(),
          email: v.email.trim(),
          password: v.password,
          locale: locale.value,
          timeZone: browserTimeZone(),
        },
      }),
    )
    sentTo.value = v.email.trim()
  })
}
</script>

<template>
  <div v-if="sentTo" data-testid="register-done" aria-live="polite">
    <AuthHeading :title="t('auth.register.doneTitle')">
      <template #icon><MailCheck aria-hidden="true" /></template>
    </AuthHeading>
    <div class="flex flex-col gap-4 text-sm leading-relaxed text-on-surface-muted">
      <i18n-t keypath="auth.register.doneText" tag="p" scope="global">
        <template #email>
          <strong class="font-semibold break-all text-on-surface">{{ sentTo }}</strong>
        </template>
      </i18n-t>
      <p>{{ t('auth.register.doneHint') }}</p>
    </div>
    <UiButton as-child size="lg" block class="mt-6">
      <RouterLink :to="loginLink" data-testid="register-to-login">
        {{ t('auth.register.toLogin') }}
      </RouterLink>
    </UiButton>
  </div>

  <div v-else>
    <AuthHeading :title="t('auth.register.title')" :description="t('auth.register.description')" />
    <form
      ref="formRef"
      class="flex flex-col gap-5"
      novalidate
      data-testid="register-form"
      @submit.prevent="submit"
    >
      <FormAlert :message="form.formError.value" />
      <UiField :label="t('auth.displayName')" :errors="form.errors('displayName')" required>
        <UiInput
          v-model="form.values.displayName"
          name="displayName"
          autocomplete="name"
          maxlength="100"
          data-testid="register-name"
        />
      </UiField>
      <UiField :label="t('auth.email')" :errors="form.errors('email')" required>
        <UiInput
          v-model="form.values.email"
          type="email"
          name="email"
          autocomplete="email"
          inputmode="email"
          data-testid="register-email"
        />
      </UiField>
      <UiField
        :label="t('auth.password')"
        :hint="t('auth.passwordHint', { min: 10 })"
        :errors="form.errors('password')"
        required
      >
        <PasswordInput
          v-model="form.values.password"
          name="password"
          autocomplete="new-password"
          data-testid="register-password"
        />
      </UiField>
      <UiButton
        type="submit"
        size="lg"
        block
        :loading="form.pending.value"
        data-testid="register-submit"
      >
        {{ t('auth.register.submit') }}
      </UiButton>
    </form>
    <p class="mt-6 text-center text-sm text-on-surface-muted">
      {{ t('auth.register.haveAccount') }}
      <TextLink :to="loginLink">{{ t('auth.register.login') }}</TextLink>
    </p>
  </div>
</template>
