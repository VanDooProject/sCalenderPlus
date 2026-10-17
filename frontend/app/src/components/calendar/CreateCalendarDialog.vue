<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { UiButton, UiDialog, UiField, UiInput, UiSelect, useToast } from '@scalenderplus/ui'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import TimeZonePicker from '@/components/TimeZonePicker.vue'
import CalendarColorPicker from './CalendarColorPicker.vue'
import { calendarsQueryKey } from '@/composables/calendars'
import { useForm, useValidators } from '@/composables/form'
import { offerUpgrade } from '@/composables/paywall'
import { useGroups } from '@/composables/groups'
import { useSession } from '@/composables/session'
import { call } from '@/lib/apiError'
import { calendarErrorMessage } from '@/lib/calendarLevels'
import { fallbackColor } from '@/lib/calendarColors'
import { asRole, canEditGroup } from '@/lib/groupRoles'

/** `POST /calendars`: a personal calendar, or one of a group the user is admin or owner of. */
const open = defineModel<boolean>('open', { default: false })
const props = withDefaults(
  defineProps<{
    /** Prefills the name (the onboarding checklist suggests one). */
    suggestedName?: string
    /** Go to the new calendar's settings afterwards (the checklist stays where it is). */
    openSettings?: boolean
  }>(),
  { suggestedName: '', openSettings: true },
)

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const queryClient = useQueryClient()
const rules = useValidators()
const { user } = useSession()
const groups = useGroups()
const formRef = ref<HTMLFormElement>()

const personal = 'personal'
const initial = () => ({
  name: props.suggestedName,
  owner: personal,
  color: fallbackColor as string,
  timeZone: user.value?.timeZone ?? 'UTC',
})
const form = useForm(initial(), {
  formRef,
  validate: (v) => ({ name: rules.required(v.name) }),
  errorMessage: (error) => calendarErrorMessage(t, error),
  // owned_calendars of the owner: me, or the group's billing owner.
  onError: (error) => offerUpgrade(error, { owner: ownerOf(submittedOwner) }),
})
let submittedOwner = personal

function ownerOf(owner: string): 'me' | 'other' {
  if (owner === personal) return 'me'
  const group = groups.data.value?.find((g) => g.id === owner)
  return group && group.billingOwnerId === user.value?.id ? 'me' : 'other'
}

const ownerOptions = computed(() => [
  { value: personal, label: t('calendarSettings.create.personal') },
  ...(groups.data.value ?? [])
    .filter((g) => canEditGroup(asRole(g.myRole)))
    .map((g) => ({ value: g.id, label: t('calendarSettings.create.inGroup', { group: g.name }) })),
])

watch(open, (isOpen) => {
  if (isOpen) form.reset(initial())
})

async function submit() {
  await form.submit(async (v) => {
    submittedOwner = v.owner
    const { data } = await call(
      api.POST('/api/v1/calendars', {
        body: {
          name: v.name.trim(),
          color: v.color,
          defaultTimeZone: v.timeZone,
          ...(v.owner === personal ? {} : { groupId: v.owner }),
        },
      }),
    )
    await queryClient.invalidateQueries({ queryKey: calendarsQueryKey, exact: true })
    open.value = false
    toast.success(t('calendarSettings.create.created', { name: data.name }))
    if (props.openSettings) {
      await router.push({ name: 'calendar-settings', params: { calendarId: data.id } })
    }
  })
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="t('calendarSettings.create.title')"
    :description="t('calendarSettings.create.description')"
    :close-label="t('common.close')"
    testid="create-calendar-dialog"
  >
    <form
      id="create-calendar-form"
      ref="formRef"
      class="flex flex-col gap-5"
      novalidate
      @submit.prevent="submit"
    >
      <FormAlert :message="form.formError.value" />
      <UiField :label="t('calendarSettings.general.name')" :errors="form.errors('name')" required>
        <UiInput
          v-model="form.values.name"
          name="name"
          maxlength="100"
          autocomplete="off"
          data-testid="calendar-name"
        />
      </UiField>
      <UiField
        :label="t('calendarSettings.create.owner')"
        :hint="t('calendarSettings.create.ownerHint')"
      >
        <UiSelect v-model="form.values.owner" :options="ownerOptions" testid="calendar-owner" />
      </UiField>
      <CalendarColorPicker
        v-model="form.values.color"
        :label="t('calendarSettings.general.color')"
      />
      <UiField
        :label="t('calendarSettings.general.timeZone')"
        :hint="t('calendarSettings.general.timeZoneHint')"
        :errors="form.errors('timeZone')"
      >
        <TimeZonePicker v-model="form.values.timeZone" />
      </UiField>
    </form>
    <template #footer>
      <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
      <UiButton
        type="submit"
        form="create-calendar-form"
        :loading="form.pending.value"
        data-testid="create-calendar-submit"
      >
        {{ t('calendarSettings.create.submit') }}
      </UiButton>
    </template>
  </UiDialog>
</template>
