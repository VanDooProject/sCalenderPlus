<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { KeyRound } from '@lucide/vue'
import { UiButton, UiCheckbox, UiField, UiInput } from '@scalenderplus/ui'
import type { MeResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import AuthHeading from '@/components/AuthHeading.vue'
import FormAlert from '@/components/FormAlert.vue'
import PasswordInput from '@/components/PasswordInput.vue'
import TextLink from '@/components/TextLink.vue'
import { useForm, useValidators } from '@/composables/form'
import { useLocaleSwitch } from '@/composables/locale'
import { setSessionUser } from '@/composables/session'
import { call } from '@/lib/apiError'
import { isApiError } from '@/lib/errorMessages'
import { safeNext } from '@/lib/redirect'
import { homeRoute } from '@/router'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()
const { setLocale } = useLocaleSwitch()
const rules = useValidators()

const step = ref<'password' | 'second-factor'>('password')
const useRecoveryCode = ref(false)
const passwordForm = ref<HTMLFormElement>()
const secondForm = ref<HTMLFormElement>()
const codeInput = ref<InstanceType<typeof UiInput>>()

const next = computed(() => safeNext(route.query.next))
const registerLink = computed(() => ({
  name: 'register',
  query: next.value ? { next: next.value } : {},
}))

const login = useForm(
  { email: '', password: '', rememberMe: false },
  {
    formRef: passwordForm,
    validate: (v) => ({ email: rules.email(v.email), password: rules.required(v.password) }),
  },
)

const second = useForm(
  { code: '', recoveryCode: '' },
  {
    formRef: secondForm,
    validate: (v) =>
      useRecoveryCode.value
        ? { recoveryCode: rules.required(v.recoveryCode) }
        : { code: rules.code(v.code) },
  },
)

async function finish(user: MeResponse) {
  setSessionUser(queryClient, user, null)
  setLocale(user.locale)
  await router.replace(next.value ?? homeRoute)
}

async function submitPassword() {
  await login.submit(async (v) => {
    const { data } = await call(
      api.POST('/api/v1/auth/login', {
        body: { email: v.email.trim(), password: v.password, rememberMe: v.rememberMe },
      }),
    )
    if (data.twoFactorRequired) {
      step.value = 'second-factor'
      await nextTick()
      codeInput.value?.focus()
    } else if (data.user) {
      await finish(data.user)
    }
  })
}

async function submitSecondFactor() {
  await second.submit(async (v) => {
    try {
      const body = useRecoveryCode.value
        ? { recoveryCode: v.recoveryCode.trim(), rememberMe: login.values.rememberMe }
        : { code: v.code.replace(/\s/g, ''), rememberMe: login.values.rememberMe }
      const { data } = await call(api.POST('/api/v1/auth/login/2fa', { body }))
      if (data.user) await finish(data.user)
    } catch (error) {
      // The pending login (5 minutes) is gone: start over with the password.
      if (isApiError(error, 'unauthenticated')) {
        backToPassword()
        login.formError.value = t('auth.login.secondFactorExpired')
        return
      }
      throw error
    }
  })
}

async function toggleRecovery() {
  useRecoveryCode.value = !useRecoveryCode.value
  second.reset()
  await nextTick()
  codeInput.value?.focus()
}

function backToPassword() {
  step.value = 'password'
  useRecoveryCode.value = false
  second.reset()
  login.values.password = ''
}
</script>

<template>
  <div v-if="step === 'password'">
    <AuthHeading :title="t('auth.login.title')" :description="t('auth.login.description')" />
    <form
      ref="passwordForm"
      class="flex flex-col gap-5"
      novalidate
      data-testid="login-form"
      @submit.prevent="submitPassword"
    >
      <FormAlert :message="login.formError.value" />
      <UiField :label="t('auth.email')" :errors="login.errors('email')" required>
        <UiInput
          v-model="login.values.email"
          type="email"
          name="email"
          autocomplete="username"
          inputmode="email"
          data-testid="login-email"
        />
      </UiField>
      <UiField :label="t('auth.password')" :errors="login.errors('password')" required>
        <template #label-end>
          <TextLink :to="{ name: 'forgot-password' }" class="text-sm">
            {{ t('auth.login.forgot') }}
          </TextLink>
        </template>
        <PasswordInput
          v-model="login.values.password"
          name="password"
          autocomplete="current-password"
          data-testid="login-password"
        />
      </UiField>
      <UiCheckbox v-model="login.values.rememberMe" :label="t('auth.login.rememberMe')" />
      <UiButton
        type="submit"
        size="lg"
        block
        :loading="login.pending.value"
        data-testid="login-submit"
      >
        {{ t('auth.login.submit') }}
      </UiButton>
    </form>
    <p class="mt-6 text-center text-sm text-on-surface-muted">
      {{ t('auth.login.noAccount') }}
      <TextLink :to="registerLink">{{ t('auth.login.register') }}</TextLink>
    </p>
  </div>

  <div v-else>
    <AuthHeading
      :title="t('auth.twoFactor.title')"
      :description="
        useRecoveryCode ? t('auth.twoFactor.recoveryDescription') : t('auth.twoFactor.description')
      "
    >
      <template #icon><KeyRound aria-hidden="true" /></template>
    </AuthHeading>
    <form
      ref="secondForm"
      class="flex flex-col gap-5"
      novalidate
      data-testid="two-factor-form"
      @submit.prevent="submitSecondFactor"
    >
      <FormAlert :message="second.formError.value" />
      <UiField
        v-if="!useRecoveryCode"
        :label="t('auth.twoFactor.code')"
        :errors="second.errors('code')"
        required
      >
        <UiInput
          ref="codeInput"
          v-model="second.values.code"
          name="code"
          inputmode="numeric"
          autocomplete="one-time-code"
          maxlength="7"
          class="font-mono tracking-[0.3em]"
          data-testid="two-factor-code"
        />
      </UiField>
      <UiField
        v-else
        :label="t('auth.twoFactor.recoveryCode')"
        :hint="t('auth.twoFactor.recoveryHint')"
        :errors="second.errors('recoveryCode')"
        required
      >
        <UiInput
          ref="codeInput"
          v-model="second.values.recoveryCode"
          name="recoveryCode"
          autocomplete="off"
          autocapitalize="characters"
          spellcheck="false"
          class="font-mono"
          data-testid="two-factor-recovery"
        />
      </UiField>
      <UiButton
        type="submit"
        size="lg"
        block
        :loading="second.pending.value"
        data-testid="two-factor-submit"
      >
        {{ t('auth.twoFactor.submit') }}
      </UiButton>
    </form>
    <div class="mt-6 flex flex-col items-center gap-3 text-sm">
      <button
        type="button"
        class="rounded font-medium text-primary underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        data-testid="two-factor-toggle-recovery"
        @click="toggleRecovery"
      >
        {{ useRecoveryCode ? t('auth.twoFactor.useCode') : t('auth.twoFactor.useRecovery') }}
      </button>
      <button
        type="button"
        class="rounded text-on-surface-muted underline-offset-2 hover:text-on-surface hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        @click="backToPassword"
      >
        {{ t('auth.twoFactor.back') }}
      </button>
    </div>
  </div>
</template>
