<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQueryClient } from '@tanstack/vue-query'
import { UiButton, UiDialog, UiField, UiSelect, useToast } from '@scalenderplus/ui'
import type { CalendarResponse, EventResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import { offerUpgrade } from '@/composables/paywall'
import { call } from '@/lib/apiError'
import { levelAtLeast } from '@/lib/calendarLevels'
import { errorMessage, isApiError } from '@/lib/errorMessages'
import {
  levelLabel,
  principalKey,
  principalLabel,
  violationMessage,
  violationsOf,
  type Violation,
} from '@/lib/eventPermissions'

/**
 * Moves an event to another calendar where I may add events (`POST /events/{id}/move`, event
 * level `manage`). Overrides travel and are re-checked in the target: `409
 * override_invalid_in_target` lists the entries that block the move.
 */
const props = defineProps<{ event: EventResponse | null; calendars: CalendarResponse[] }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ moved: [event: EventResponse] }>()

const { t } = useI18n()
const toast = useToast()
const queryClient = useQueryClient()

const target = ref<string>()
const error = ref<string | null>(null)
const blocking = ref<Violation[]>([])
const busy = ref(false)

watch(open, (value) => {
  if (!value) return
  target.value = undefined
  error.value = null
  blocking.value = []
})

const options = computed(() =>
  props.calendars
    .filter(
      (c) => c.id !== props.event?.calendarId && levelAtLeast(c.myLevel, 'contribute') && !c.frozen,
    )
    .map((c) => ({ value: c.id, label: c.name })),
)

async function move() {
  const event = props.event
  if (!event || !target.value) {
    error.value = t('event.move.pick')
    return
  }
  busy.value = true
  error.value = null
  blocking.value = []
  try {
    const { data } = await call(
      api.POST('/api/v1/events/{id}/move', {
        params: { path: { id: event.id }, header: { 'If-Match': event.etag ?? '*' } },
        body: { targetCalendarId: target.value },
      }),
    )
    toast.success(t('event.moved'))
    open.value = false
    emit('moved', data)
    await queryClient.invalidateQueries({ queryKey: ['events'] })
  } catch (e) {
    if (isApiError(e, 'override_invalid_in_target')) {
      blocking.value = violationsOf(e)
      error.value = t('event.move.blocked')
    } else if (isApiError(e, 'precondition_failed')) {
      error.value = t('event.move.changed')
      await queryClient.invalidateQueries({ queryKey: ['events'] })
    } else {
      // A target with another owner counts the event's permissions against its plan.
      offerUpgrade(e)
      error.value = errorMessage(t, e)
    }
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="t('event.move.title', { title: event?.title ?? '' })"
    :description="t('event.move.description')"
    :close-label="t('common.close')"
    testid="event-move-dialog"
  >
    <form id="event-move-form" class="flex flex-col gap-4" novalidate @submit.prevent="move">
      <FormAlert :message="error" />
      <ul
        v-if="blocking.length"
        class="flex list-disc flex-col gap-1 pl-5 text-sm"
        data-testid="event-move-blocking"
      >
        <li v-for="v in blocking" :key="`${principalKey(v.principal)}-${v.reason}`">
          <span class="font-medium">{{
            principalLabel(t, v.principal, v.principalName ?? null)
          }}</span>
          <template v-if="v.level"> ({{ levelLabel(t, v.level) }})</template>:
          {{ violationMessage(t, v.reason) }}
        </li>
      </ul>
      <UiField :label="t('event.move.target')">
        <UiSelect
          v-model="target"
          :options="options"
          :placeholder="t('event.move.targetPlaceholder')"
          testid="event-move-target"
        />
      </UiField>
    </form>
    <template #footer>
      <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
      <UiButton
        type="submit"
        form="event-move-form"
        :loading="busy"
        data-testid="event-move-submit"
      >
        {{ t('event.move.submit') }}
      </UiButton>
    </template>
  </UiDialog>
</template>
