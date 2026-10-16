<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQueryClient } from '@tanstack/vue-query'
import { ChevronDown, Repeat, Trash2 } from '@lucide/vue'
import type { components, EventResponse } from '@scalenderplus/api-client'
import {
  UiAlert,
  UiButton,
  UiCheckbox,
  UiDialog,
  UiField,
  UiInput,
  UiSelect,
  useToast,
} from '@scalenderplus/ui'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import TimeZonePicker from '@/components/TimeZonePicker.vue'
import { useForm, useValidators } from '@/composables/form'
import { call } from '@/lib/apiError'
import { errorMessage, isApiError } from '@/lib/errorMessages'
import { describeRule } from '@/lib/recurrenceText'
import {
  endBeforeStart,
  eventValues,
  isRuleOccurrence,
  shiftSeriesTimes,
  toCreateRequest,
  toMergePatch,
  toOccurrencePatch,
  type EventFormValues,
} from '@/lib/eventForm'

type Warning = components['schemas']['EventWarningResponse']
type Scope = 'this' | 'following' | 'all'

/**
 * Create (`event` null, `initial` the selected slot) or edit an event: merge-patch with the list
 * `etag` as `If-Match` (`412`: the latest version is loaded and the edits are kept); occurrences of
 * a series ask "this event / this and following / all events" first.
 */
const props = defineProps<{
  event: EventResponse | null
  initial: EventFormValues | null
  calendars: { value: string; label: string }[]
  timeZone: string
}>()
const open = defineModel<boolean>('open', { default: false })

const { t } = useI18n()
const toast = useToast()
const queryClient = useQueryClient()
const rules = useValidators()
const formRef = ref<HTMLFormElement>()

/** The api's limit of cover titles (privacy-levels.md §5). */
const coverTitleMax = 200

const empty: EventFormValues = {
  title: '',
  coverTitle: '',
  calendarId: '',
  allDay: false,
  startDate: '',
  startTime: '',
  endDate: '',
  endTime: '',
  timeZone: 'UTC',
  location: '',
  description: '',
}

/** The values the form started from (after a `412`: the latest version). */
const base = shallowRef<EventFormValues>(empty)
const etag = ref<string | null>(null)
const showMore = ref(false)
const step = ref<'form' | 'scope' | 'delete'>('form')
const scope = ref<Scope>('this')
const warnings = ref<Warning[]>([])
const deleting = ref(false)

const form = useForm<EventFormValues>(
  { ...empty },
  {
    formRef,
    validate: (v) => ({
      title: rules.required(v.title),
      coverTitle:
        v.coverTitle.trim().length > coverTitleMax
          ? t('validation.maxLength', { max: coverTitleMax })
          : undefined,
      calendarId: v.calendarId ? undefined : t('validation.required'),
      endDate: endBeforeStart(v) ? t('event.form.endBeforeStart') : undefined,
    }),
  },
)

const isEdit = computed(() => props.event !== null)
const isOccurrence = computed(() => !!props.event?.recurrenceId)
const canSplit = computed(() => (props.event ? isRuleOccurrence(props.event) : false))
const rule = computed(() =>
  props.event?.recurrence?.rrule ? describeRule(t, props.event.recurrence.rrule) : null,
)

watch(open, (isOpen) => {
  if (!isOpen) return
  const values = props.event
    ? eventValues(props.event, props.timeZone)
    : (props.initial ?? { ...empty, timeZone: props.timeZone })
  base.value = { ...values }
  etag.value = props.event?.etag ?? null
  form.reset({ ...values })
  showMore.value = values.timeZone !== props.timeZone
  step.value = 'form'
  scope.value = 'this'
  warnings.value = []
  // After the dialog's own initial focus: start in the title.
  void nextTick(() =>
    requestAnimationFrame(() =>
      document.querySelector<HTMLElement>('[data-testid="event-title"]')?.focus(),
    ),
  )
})

const scopeOptions = computed(() => [
  { value: 'this' as const, label: t('event.scope.this') },
  ...(canSplit.value ? [{ value: 'following' as const, label: t('event.scope.following') }] : []),
  { value: 'all' as const, label: t('event.scope.all') },
])

async function invalidate() {
  await queryClient.invalidateQueries({ queryKey: ['events'] })
}

function finish(result: { warnings?: Warning[] | null }, message: string) {
  void invalidate()
  if (result.warnings?.length) {
    // Keep the dialog open so the shifted time is noticed.
    warnings.value = result.warnings
    return
  }
  open.value = false
  toast.success(message)
}

/** `412`: someone changed the event; load the latest version and keep the user's edits. */
async function reloadAfterConflict() {
  const event = props.event
  if (!event) return
  try {
    const { data } = await call(
      api.GET('/api/v1/events/{id}', { params: { path: { id: event.id } } }),
    )
    etag.value = data.etag ?? null
    if (!event.recurrenceId) base.value = eventValues(data, props.timeZone)
  } catch {
    // The toast below still asks to try again.
  }
  void invalidate()
  toast.show({ variant: 'error', title: t('errors.code.precondition_failed') })
}

async function create(v: EventFormValues) {
  const { data } = await call(api.POST('/api/v1/events', { body: toCreateRequest(v) }))
  finish(data, t('event.created', { title: data.title ?? '' }))
}

async function update(v: EventFormValues, chosen: Scope) {
  const event = props.event!
  const ifMatch = etag.value ?? '*'
  if (!event.recurrenceId) {
    const body = toMergePatch(base.value, v)
    const { data } = await call(
      api.PATCH('/api/v1/events/{id}', {
        params: { path: { id: event.id }, header: { 'If-Match': ifMatch } },
        body,
      }),
    )
    finish(data, t('event.saved'))
    return
  }
  if (chosen === 'this') {
    const { data } = await call(
      api.PATCH('/api/v1/events/{id}/occurrences/{recurrenceId}', {
        params: {
          path: { id: event.id, recurrenceId: event.recurrenceId },
          header: { 'If-Match': ifMatch },
        },
        body: toOccurrencePatch(base.value, v),
      }),
    )
    finish(data, t('event.saved'))
  } else if (chosen === 'following') {
    const { data } = await call(
      api.POST('/api/v1/events/{id}/split', {
        params: { path: { id: event.id }, header: { 'If-Match': ifMatch } },
        body: { recurrenceId: event.recurrenceId, ...toMergePatch(base.value, v) },
      }),
    )
    finish(data, t('event.saved'))
  } else {
    const body = toMergePatch(base.value, v)
    if (body.start || body.end) {
      // The series starts at its first occurrence: move it by the same amount as this one.
      const { data: master } = await call(
        api.GET('/api/v1/events/{id}', { params: { path: { id: event.id } } }),
      )
      Object.assign(body, shiftSeriesTimes(master, base.value, v))
    }
    const { data } = await call(
      api.PATCH('/api/v1/events/{id}', {
        params: { path: { id: event.id }, header: { 'If-Match': ifMatch } },
        body,
      }),
    )
    finish(data, t('event.saved'))
  }
}

async function submit() {
  if (isEdit.value && isOccurrence.value && step.value === 'form') {
    // Validate first, then ask which occurrences the change is for.
    const ok = await form.submit(async () => {})
    if (!ok) return
    step.value = 'scope'
    await nextTick()
    document.querySelector<HTMLElement>('[data-testid="event-scope"] input:checked')?.focus()
    return
  }
  const chosen = scope.value
  const ok = await form.submit(async (v) => {
    if (!isEdit.value) await create(v)
    else await update(v, chosen)
  })
  if (!ok) {
    step.value = 'form'
    if (isApiError(form.lastError.value, 'precondition_failed')) await reloadAfterConflict()
  }
}

async function remove(chosen: Scope) {
  const event = props.event
  if (!event) return
  deleting.value = true
  try {
    const header = { 'If-Match': etag.value ?? '*' }
    if (event.recurrenceId && chosen === 'this') {
      await call(
        api.DELETE('/api/v1/events/{id}/occurrences/{recurrenceId}', {
          params: { path: { id: event.id, recurrenceId: event.recurrenceId }, header },
        }),
      )
    } else {
      await call(api.DELETE('/api/v1/events/{id}', { params: { path: { id: event.id }, header } }))
    }
    void invalidate()
    open.value = false
    toast.success(t('event.deleted'))
  } catch (error) {
    if (isApiError(error, 'precondition_failed')) await reloadAfterConflict()
    else toast.show({ variant: 'error', title: errorMessage(t, error) })
  } finally {
    deleting.value = false
  }
}

const textareaClass =
  'min-h-20 w-full rounded-lg border border-border-strong bg-surface-raised px-3 py-2 text-base text-on-surface shadow-xs placeholder:text-on-surface-muted hover:border-on-surface-muted focus:outline-2 focus:-outline-offset-1 focus:outline-focus sm:text-sm'
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="isEdit ? t('event.editTitle') : t('event.newTitle')"
    :close-label="t('common.close')"
    size="lg"
    testid="event-dialog"
  >
    <div v-if="warnings.length" class="flex flex-col gap-3" data-testid="event-warnings">
      <UiAlert v-for="w in warnings" :key="w.code + w.field" variant="warning" role="status">
        {{
          w.code === 'time_shifted_dst_gap'
            ? t('event.warnings.shifted', {
                requested: w.requested.slice(11, 16),
                resolved: w.resolved.slice(11, 16),
              })
            : t('event.warnings.ambiguous', { requested: w.requested.slice(11, 16) })
        }}
      </UiAlert>
    </div>

    <div v-else-if="step === 'delete'" class="flex flex-col gap-4" data-testid="event-delete">
      <p class="text-sm">
        {{ t('event.deleteConfirm', { title: event?.title ?? '' }) }}
      </p>
      <div v-if="isOccurrence" class="flex flex-col gap-2">
        <UiButton
          variant="danger"
          :loading="deleting"
          data-testid="event-delete-this"
          @click="remove('this')"
        >
          {{ t('event.scope.deleteThis') }}
        </UiButton>
        <UiButton
          variant="secondary"
          :loading="deleting"
          data-testid="event-delete-all"
          @click="remove('all')"
        >
          {{ t('event.scope.deleteAll') }}
        </UiButton>
      </div>
    </div>

    <form
      v-else
      id="event-form"
      ref="formRef"
      class="flex flex-col gap-5"
      novalidate
      @submit.prevent="submit"
    >
      <FormAlert :message="form.formError.value" />

      <fieldset v-if="step === 'scope'" class="flex flex-col gap-2" data-testid="event-scope">
        <legend class="mb-2 text-sm font-medium">{{ t('event.scope.label') }}</legend>
        <label
          v-for="o in scopeOptions"
          :key="o.value"
          class="flex cursor-pointer items-center gap-3 rounded-lg border border-border px-3 py-2 text-sm has-[:checked]:border-primary"
        >
          <input
            v-model="scope"
            type="radio"
            name="scope"
            :value="o.value"
            :data-testid="`event-scope-${o.value}`"
          />
          {{ o.label }}
        </label>
      </fieldset>

      <template v-else>
        <UiField :label="t('event.form.title')" :errors="form.errors('title')" required>
          <UiInput
            v-model="form.values.title"
            name="title"
            maxlength="500"
            autocomplete="off"
            data-autofocus
            data-testid="event-title"
          />
        </UiField>
        <UiField
          :label="t('event.form.coverTitle')"
          :hint="t('event.form.coverTitleHint')"
          :errors="form.errors('coverTitle')"
        >
          <UiInput
            v-model="form.values.coverTitle"
            name="coverTitle"
            :maxlength="coverTitleMax"
            autocomplete="off"
            data-testid="event-cover-title"
          />
        </UiField>
        <UiField :label="t('event.form.calendar')" :errors="form.errors('calendarId')" required>
          <UiSelect
            v-model="form.values.calendarId"
            :options="calendars"
            :disabled="isEdit"
            testid="event-calendar"
          />
        </UiField>
        <UiCheckbox
          v-model="form.values.allDay"
          :label="t('event.form.allDay')"
          data-testid="event-all-day"
        />
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <UiField :label="t('event.form.startDate')">
            <UiInput v-model="form.values.startDate" type="date" data-testid="event-start-date" />
          </UiField>
          <UiField v-if="!form.values.allDay" :label="t('event.form.startTime')">
            <UiInput v-model="form.values.startTime" type="time" data-testid="event-start-time" />
          </UiField>
          <UiField :label="t('event.form.endDate')" :errors="form.errors('endDate')">
            <UiInput v-model="form.values.endDate" type="date" data-testid="event-end-date" />
          </UiField>
          <UiField v-if="!form.values.allDay" :label="t('event.form.endTime')">
            <UiInput v-model="form.values.endTime" type="time" data-testid="event-end-time" />
          </UiField>
        </div>
        <p
          v-if="rule"
          class="-mt-2 flex items-center gap-1.5 text-sm text-on-surface-muted"
          data-testid="event-rule"
        >
          <Repeat class="size-3.5" aria-hidden="true" />{{ rule }}
        </p>
        <UiField :label="t('event.form.location')">
          <UiInput
            v-model="form.values.location"
            maxlength="500"
            autocomplete="off"
            data-testid="event-location"
          />
        </UiField>
        <UiField id="event-description" :label="t('event.form.description')">
          <textarea
            id="event-description"
            v-model="form.values.description"
            rows="3"
            maxlength="10000"
            :class="textareaClass"
            data-testid="event-description"
          />
        </UiField>
        <div v-if="!form.values.allDay">
          <button
            type="button"
            class="inline-flex items-center gap-1 rounded-sm text-sm font-medium text-on-surface-muted hover:text-on-surface focus-visible:outline-2 focus-visible:outline-focus"
            :aria-expanded="showMore"
            aria-controls="event-more"
            data-testid="event-more"
            @click="showMore = !showMore"
          >
            <ChevronDown
              class="size-4 transition-transform"
              :class="showMore ? 'rotate-180' : ''"
              aria-hidden="true"
            />
            {{ t('event.form.more') }}
          </button>
          <div v-if="showMore" id="event-more" class="mt-3">
            <UiField :label="t('event.form.timeZone')" :hint="t('event.form.timeZoneHint')">
              <TimeZonePicker v-model="form.values.timeZone" />
            </UiField>
          </div>
        </div>
      </template>
    </form>

    <template #footer>
      <template v-if="warnings.length">
        <UiButton data-testid="event-warnings-close" @click="open = false">
          {{ t('common.close') }}
        </UiButton>
      </template>
      <template v-else-if="step === 'delete'">
        <UiButton variant="ghost" @click="step = 'form'">{{ t('common.cancel') }}</UiButton>
        <UiButton
          v-if="!isOccurrence"
          variant="danger"
          :loading="deleting"
          data-testid="event-delete-confirm"
          @click="remove('all')"
        >
          {{ t('event.delete') }}
        </UiButton>
      </template>
      <template v-else>
        <UiButton
          v-if="isEdit && step === 'form'"
          variant="ghost"
          class="mr-auto"
          data-testid="event-delete"
          @click="step = 'delete'"
        >
          <Trash2 class="size-4" aria-hidden="true" />{{ t('event.delete') }}
        </UiButton>
        <UiButton variant="ghost" @click="step === 'scope' ? (step = 'form') : (open = false)">
          {{ step === 'scope' ? t('event.back') : t('common.cancel') }}
        </UiButton>
        <UiButton
          type="submit"
          form="event-form"
          :loading="form.pending.value"
          data-testid="event-save"
        >
          {{ step === 'scope' ? t('event.saveScope') : t('event.save') }}
        </UiButton>
      </template>
    </template>
  </UiDialog>
</template>
