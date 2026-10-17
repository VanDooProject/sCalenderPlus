<script setup lang="ts">
import { computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { CalendarX2 } from '@lucide/vue'
import { UiAlert, UiButton, UiSpinner } from '@scalenderplus/ui'
import EmptyState from '@/components/EmptyState.vue'
import { eventLinkDate, eventLinkQuery } from '@/composables/eventLink'
import { useSession } from '@/composables/session'
import { errorMessage, isApiError } from '@/lib/errorMessages'

/**
 * `/e/{id}` and `/e/{id}/edit`: loads the event and replaces itself with the calendar at the
 * event's date, which opens the details (or the edit) dialog. Deleted events and events the user
 * no longer sees (`404`, also `403`) get an explanation instead of an error.
 */
const props = defineProps<{ edit?: boolean }>()

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const { user } = useSession()

const id = computed(() => String(route.params.eventId ?? ''))
const query = useQuery(computed(() => eventLinkQuery(id.value)))
const gone = computed(() => isApiError(query.error.value, 'not_found', 'insufficient_permission'))

watch(
  () => query.data.value,
  (event) => {
    if (!event) return
    const date = eventLinkDate(event, user.value?.timeZone ?? 'UTC').toString()
    void router.replace({
      name: 'calendar',
      query: { date, event: event.id, ...(props.edit ? { edit: '1' } : {}) },
    })
  },
  { immediate: true },
)
</script>

<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-6" data-testid="event-link">
    <EmptyState
      v-if="gone"
      :title="t('eventLink.goneTitle')"
      :description="t('eventLink.goneText')"
      data-testid="event-link-gone"
    >
      <template #icon><CalendarX2 aria-hidden="true" /></template>
      <UiButton as-child variant="secondary">
        <RouterLink :to="{ name: 'calendar' }">{{ t('eventLink.toCalendar') }}</RouterLink>
      </UiButton>
    </EmptyState>
    <template v-else-if="query.isError.value">
      <h1 class="text-2xl font-semibold tracking-tight">{{ t('eventLink.title') }}</h1>
      <UiAlert variant="danger" role="alert">
        <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <span>{{ errorMessage(t, query.error.value) }}</span>
          <UiButton variant="secondary" size="sm" @click="query.refetch()">
            {{ t('common.retry') }}
          </UiButton>
        </div>
      </UiAlert>
    </template>
    <div v-else class="flex justify-center py-16">
      <UiSpinner class="size-6 text-primary" :label="t('eventLink.opening')" />
    </div>
  </div>
</template>
