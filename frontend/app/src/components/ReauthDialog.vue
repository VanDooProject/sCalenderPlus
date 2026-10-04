<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { UiButton, UiDialog, UiField, UiInput } from '@scalenderplus/ui'
import FormAlert from './FormAlert.vue'
import PasswordInput from './PasswordInput.vue'
import { useForm, useValidators } from '@/composables/form'

export interface ReauthBody {
  password?: string
  code?: string
}

/** Confirms a sensitive change with the password or a current authenticator code. */
const props = defineProps<{
  title: string
  description: string
  confirmLabel: string
  danger?: boolean
  testid?: string
  action: (body: ReauthBody) => Promise<void>
}>()
const open = defineModel<boolean>('open', { default: false })
const { t } = useI18n()
const rules = useValidators()
const formRef = ref<HTMLFormElement>()
const method = ref<'password' | 'code'>('password')

const form = useForm(
  { password: '', code: '' },
  {
    formRef,
    validate: (v) =>
      method.value === 'password'
        ? { password: rules.required(v.password) }
        : { code: rules.code(v.code) },
  },
)

watch(open, (isOpen) => {
  if (isOpen) {
    method.value = 'password'
    form.reset()
  }
})

async function submit() {
  const ok = await form.submit((v) =>
    props.action(
      method.value === 'password' ? { password: v.password } : { code: v.code.replace(/\s/g, '') },
    ),
  )
  if (ok) open.value = false
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="title"
    :description="description"
    :close-label="t('common.close')"
    :testid="testid"
  >
    <form ref="formRef" class="flex flex-col gap-4" novalidate @submit.prevent="submit">
      <FormAlert :message="form.formError.value" />
      <UiField
        v-if="method === 'password'"
        :label="t('auth.currentPassword')"
        :errors="form.errors('password')"
        required
      >
        <PasswordInput
          v-model="form.values.password"
          autocomplete="current-password"
          data-testid="reauth-password"
        />
      </UiField>
      <UiField v-else :label="t('auth.twoFactor.code')" :errors="form.errors('code')" required>
        <UiInput
          v-model="form.values.code"
          inputmode="numeric"
          autocomplete="one-time-code"
          maxlength="7"
          class="font-mono tracking-[0.3em]"
          data-testid="reauth-code"
        />
      </UiField>
      <button
        type="button"
        class="self-start rounded text-sm font-medium text-primary underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        @click="method = method === 'password' ? 'code' : 'password'"
      >
        {{
          method === 'password'
            ? t('settings.security.useCodeInstead')
            : t('settings.security.usePasswordInstead')
        }}
      </button>
      <div class="mt-2 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
        <UiButton
          type="submit"
          :variant="danger ? 'danger' : 'primary'"
          :loading="form.pending.value"
          data-testid="reauth-submit"
        >
          {{ confirmLabel }}
        </UiButton>
      </div>
    </form>
  </UiDialog>
</template>
