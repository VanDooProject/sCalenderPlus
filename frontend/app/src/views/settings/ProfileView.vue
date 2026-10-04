<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQueryClient } from '@tanstack/vue-query'
import { BadgeCheck, MailWarning } from '@lucide/vue'
import { UiButton, UiField, UiInput, UiSelect, useToast } from '@scalenderplus/ui'
import type { MeResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import SettingsCard from '@/components/SettingsCard.vue'
import TimeZonePicker from '@/components/TimeZonePicker.vue'
import { useForm, useValidators } from '@/composables/form'
import { useLocaleSwitch } from '@/composables/locale'
import { fetchSession, sessionQueryKey, useSession, type Session } from '@/composables/session'
import { supportedLocales } from '@/i18n'
import { call } from '@/lib/apiError'
import { isApiError } from '@/lib/errorMessages'

const weekdays = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday']

const { t, locale } = useI18n()
const toast = useToast()
const queryClient = useQueryClient()
const { setLocale } = useLocaleSwitch()
const { session, user } = useSession()
const rules = useValidators()
const formRef = ref<HTMLFormElement>()

type Profile = Pick<MeResponse, 'displayName' | 'locale' | 'timeZone' | 'weekStart'>
const fromUser = (u: MeResponse): Profile => ({
  displayName: u.displayName,
  locale: u.locale,
  timeZone: u.timeZone,
  weekStart: u.weekStart,
})

const form = useForm<Profile>(
  user.value
    ? fromUser(user.value)
    : { displayName: '', locale: 'en', timeZone: 'UTC', weekStart: 'monday' },
  { formRef, validate: (v) => ({ displayName: rules.required(v.displayName) }) },
)

const dirty = computed(() => {
  if (!user.value) return false
  const saved = fromUser(user.value)
  return (Object.keys(saved) as (keyof Profile)[]).some((key) => saved[key] !== form.values[key])
})

// Follow the stored profile while there are no unsaved edits (e.g. after a refetch).
watch(user, (current) => {
  if (current && !dirty.value && !form.pending.value) form.reset(fromUser(current))
})

const localeOptions = computed(() =>
  supportedLocales.map((code) => ({
    value: code,
    label: new Intl.DisplayNames([code], { type: 'language' }).of(code) ?? code,
  })),
)

const weekStartOptions = computed(() => {
  const format = new Intl.DateTimeFormat(locale.value, { weekday: 'long', timeZone: 'UTC' })
  // 2026-01-05 is a Monday.
  return weekdays.map((value, i) => ({
    value,
    label: format.format(new Date(Date.UTC(2026, 0, 5 + i))),
  }))
})

/** The ETag for `If-Match`; a session from a login response has none yet. */
async function currentEtag(): Promise<string> {
  if (session.value?.etag) return session.value.etag
  const fresh = await queryClient.fetchQuery({ queryKey: sessionQueryKey, queryFn: fetchSession })
  return fresh?.etag ?? '*'
}

async function save() {
  await form.submit(async (v) => {
    const saved = user.value ? fromUser(user.value) : null
    const patch = Object.fromEntries(
      (Object.keys(v) as (keyof Profile)[])
        .filter((key) => !saved || saved[key] !== v[key])
        .map((key) => [key, key === 'displayName' ? v[key].trim() : v[key]]),
    )
    try {
      const { data, response } = await call(
        api.PATCH('/api/v1/me', {
          body: patch,
          headers: { 'If-Match': await currentEtag() },
        }),
      )
      const next: Session = { user: data, etag: response.headers.get('ETag') }
      queryClient.setQueryData(sessionQueryKey, () => next)
      form.reset(fromUser(data))
      setLocale(data.locale)
      toast.success(t('settings.profile.saved'))
    } catch (error) {
      // Changed elsewhere: load the current version (new ETag); the edits stay for another try.
      if (isApiError(error, 'precondition_failed')) {
        await queryClient.invalidateQueries({ queryKey: sessionQueryKey })
      }
      throw error
    }
  })
}

function discard() {
  if (user.value) form.reset(fromUser(user.value))
}
</script>

<template>
  <form ref="formRef" novalidate data-testid="profile-form" @submit.prevent="save">
    <SettingsCard
      :title="t('settings.profile.title')"
      :description="t('settings.profile.description')"
    >
      <div class="flex flex-col gap-5">
        <FormAlert :message="form.formError.value" />
        <div v-if="user" class="flex flex-col gap-1.5">
          <span class="text-sm font-medium">{{ t('auth.email') }}</span>
          <p class="flex flex-wrap items-center gap-2 text-sm">
            <span class="break-all" data-testid="profile-email">{{ user.email }}</span>
            <span
              v-if="user.emailVerified"
              class="inline-flex items-center gap-1 rounded-full bg-success-soft px-2 py-0.5 text-xs font-medium text-on-success-soft"
            >
              <BadgeCheck class="size-3.5" aria-hidden="true" />{{ t('settings.profile.verified') }}
            </span>
            <span
              v-else
              class="inline-flex items-center gap-1 rounded-full bg-warning-soft px-2 py-0.5 text-xs font-medium text-on-warning-soft"
            >
              <MailWarning class="size-3.5" aria-hidden="true" />
              {{ t('settings.profile.unverified') }}
            </span>
          </p>
        </div>
        <UiField :label="t('auth.displayName')" :errors="form.errors('displayName')" required>
          <UiInput
            v-model="form.values.displayName"
            name="displayName"
            autocomplete="name"
            maxlength="100"
            data-testid="profile-name"
          />
        </UiField>
        <div class="grid gap-5 sm:grid-cols-2">
          <UiField
            :label="t('settings.profile.locale')"
            :hint="t('settings.profile.localeHint')"
            :errors="form.errors('locale')"
          >
            <UiSelect
              v-model="form.values.locale"
              :options="localeOptions"
              testid="profile-locale"
            />
          </UiField>
          <UiField :label="t('settings.profile.weekStart')" :errors="form.errors('weekStart')">
            <UiSelect
              v-model="form.values.weekStart"
              :options="weekStartOptions"
              testid="profile-week-start"
            />
          </UiField>
        </div>
        <UiField
          :label="t('settings.profile.timeZone')"
          :hint="t('settings.profile.timeZoneHint')"
          :errors="form.errors('timeZone')"
        >
          <TimeZonePicker v-model="form.values.timeZone" />
        </UiField>
      </div>
      <template #footer>
        <UiButton variant="ghost" :disabled="!dirty || form.pending.value" @click="discard">
          {{ t('common.discard') }}
        </UiButton>
        <UiButton
          type="submit"
          :disabled="!dirty"
          :loading="form.pending.value"
          data-testid="profile-save"
        >
          {{ t('common.save') }}
        </UiButton>
      </template>
    </SettingsCard>
  </form>
</template>
