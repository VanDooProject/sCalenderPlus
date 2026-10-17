<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { UiButton, UiDialog, UiField, UiInput, UiSelect } from '@scalenderplus/ui'
import FormAlert from '@/components/FormAlert.vue'
import FeedAddressReveal from '@/components/feeds/FeedAddressReveal.vue'
import { useCalendars } from '@/composables/calendars'
import {
  feedScopes,
  labelModes,
  pastDaysOptions,
  useFeedActions,
  type FeedScope,
  type IssuedFeedToken,
  type LabelMode,
} from '@/composables/feeds'
import { errorMessage, isApiError } from '@/lib/errorMessages'

/**
 * New feed link: what it shows (everything — recommended —, one calendar, or only what is shared
 * with me), how read-only events are marked, an optional name and how far back it reaches. The
 * secret address is shown once in the same dialog after creating.
 */
const props = defineProps<{ presetCalendarId?: string }>()
const open = defineModel<boolean>('open', { default: false })

const { t } = useI18n()
const calendars = useCalendars()
const { create } = useFeedActions()

const scope = ref<string>('aggregate' satisfies FeedScope)
const calendarId = ref<string>()
const name = ref('')
const labelMode = ref<string>('prefix_emoji' satisfies LabelMode)
const pastDays = ref('90')
const pending = ref(false)
const error = ref<string | null>(null)
const calendarError = ref<string | null>(null)
const issued = ref<IssuedFeedToken | null>(null)

watch(open, (isOpen) => {
  if (!isOpen) return
  scope.value = props.presetCalendarId ? 'calendar' : 'aggregate'
  calendarId.value = props.presetCalendarId
  name.value = ''
  labelMode.value = 'prefix_emoji'
  pastDays.value = '90'
  error.value = null
  calendarError.value = null
  issued.value = null
})

const scopeOptions = computed(() =>
  feedScopes.map((s) => ({ value: s as string, label: t(`feeds.create.scopes.${s}`) })),
)
const calendarOptions = computed(() =>
  (calendars.data.value ?? []).map((c) => ({ value: c.id, label: c.name })),
)
const labelOptions = computed(() =>
  labelModes.map((m) => ({ value: m as string, label: t(`feeds.labelModes.${m}.label`) })),
)
const pastDaysSelectOptions = computed(() =>
  pastDaysOptions.map((d) => ({ value: String(d), label: t('feeds.create.pastDaysOption', d) })),
)

async function submit() {
  error.value = null
  calendarError.value = null
  if (scope.value === 'calendar' && !calendarId.value) {
    calendarError.value = t('feeds.create.pickCalendar')
    return
  }
  pending.value = true
  try {
    issued.value = await create({
      scope: scope.value,
      ...(scope.value === 'calendar' ? { calendarId: calendarId.value } : {}),
      ...(name.value.trim() ? { name: name.value.trim() } : {}),
      labelMode: labelMode.value,
      includePastDays: Number(pastDays.value),
    })
  } catch (e) {
    error.value = isApiError(e, 'conflict') ? t('feeds.errors.limit') : errorMessage(t, e)
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="issued ? t('feeds.reveal.title') : t('feeds.create.title')"
    :description="issued ? undefined : t('feeds.create.description')"
    :close-label="t('common.close')"
    size="lg"
    testid="create-feed-dialog"
  >
    <FeedAddressReveal v-if="issued" :address="issued" testid="created-feed" />
    <form
      v-else
      id="create-feed-form"
      data-testid="create-feed-form"
      class="flex flex-col gap-5"
      novalidate
      @submit.prevent="submit"
    >
      <FormAlert :message="error" />
      <UiField :label="t('feeds.create.scope')" :hint="t(`feeds.create.scopeHints.${scope}`)">
        <UiSelect v-model="scope" :options="scopeOptions" testid="feed-scope" />
      </UiField>
      <UiField
        v-if="scope === 'calendar'"
        :label="t('feeds.create.calendar')"
        :errors="calendarError"
        required
      >
        <UiSelect
          v-model="calendarId"
          :options="calendarOptions"
          :placeholder="t('feeds.create.calendarPlaceholder')"
          testid="feed-calendar"
        />
      </UiField>
      <UiField
        :label="t('feeds.create.labelMode')"
        :hint="t(`feeds.labelModes.${labelMode}.example`)"
      >
        <UiSelect v-model="labelMode" :options="labelOptions" testid="feed-label-mode" />
      </UiField>
      <div class="grid gap-5 sm:grid-cols-2">
        <UiField :label="t('feeds.create.name')" :hint="t('feeds.create.nameHint')">
          <UiInput v-model="name" maxlength="100" autocomplete="off" data-testid="feed-name" />
        </UiField>
        <UiField :label="t('feeds.create.pastDays')">
          <UiSelect v-model="pastDays" :options="pastDaysSelectOptions" testid="feed-past-days" />
        </UiField>
      </div>
    </form>
    <template #footer>
      <template v-if="issued">
        <UiButton data-testid="created-feed-done" @click="open = false">
          {{ t('feeds.reveal.done') }}
        </UiButton>
      </template>
      <template v-else>
        <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
        <UiButton
          type="submit"
          form="create-feed-form"
          :loading="pending"
          data-testid="create-feed-submit"
        >
          {{ t('feeds.create.submit') }}
        </UiButton>
      </template>
    </template>
  </UiDialog>
</template>
