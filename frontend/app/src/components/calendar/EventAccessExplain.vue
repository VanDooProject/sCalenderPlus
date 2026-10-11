<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery } from '@tanstack/vue-query'
import { UiField, UiSelect } from '@scalenderplus/ui'
import type { EventResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import { usePrincipalCandidates } from '@/composables/eventPermissions'
import { call } from '@/lib/apiError'
import { errorMessage } from '@/lib/errorMessages'
import { decisiveStep, levelLabel, stepSentence } from '@/lib/eventPermissions'

/**
 * "Why can X see this?" (api.md "Access explainer"): the engine's steps as a timeline with the
 * deciding step marked. Everyone explains their own level; calendar managers also other people.
 */
const props = defineProps<{ event: EventResponse; canExplainOthers: boolean }>()

const { t } = useI18n()
const me = 'me'
const person = ref<string>(me)
watch(
  () => props.event.id,
  () => (person.value = me),
)

const { people } = usePrincipalCandidates(() => props.canExplainOthers)
const personOptions = computed(() => [
  { value: me, label: t('permissions.explain.me') },
  ...people.value.map((p) => ({ value: p.id, label: p.name })),
])

const explanation = useQuery(
  computed(() => ({
    queryKey: ['event-access', props.event.id, person.value],
    queryFn: async () =>
      (
        await call(
          api.GET('/api/v1/events/{id}/access/explain', {
            params: {
              path: { id: props.event.id },
              query: person.value === me ? {} : { userId: person.value },
            },
          }),
        )
      ).data,
    retry: false,
  })),
)

const steps = computed(() => explanation.data.value?.steps ?? [])
const decisive = computed(() => decisiveStep(steps.value))
const who = computed(() =>
  person.value === me
    ? t('permissions.explain.you')
    : (explanation.data.value?.user.displayName ??
      people.value.find((p) => p.id === person.value)?.name ??
      ''),
)
const summary = computed(() => {
  const data = explanation.data.value
  if (!data) return ''
  if (data.hiddenAsTransparent)
    return t('permissions.explain.hiddenAsTransparent', { name: who.value })
  return t(person.value === me ? 'permissions.explain.resultMe' : 'permissions.explain.result', {
    name: who.value,
    level: levelLabel(t, data.level),
  })
})
</script>

<template>
  <div class="flex flex-col gap-4 text-sm" data-testid="event-explain">
    <UiField v-if="canExplainOthers" :label="t('permissions.explain.person')">
      <UiSelect v-model="person" :options="personOptions" testid="explain-person" />
    </UiField>
    <p v-if="explanation.isPending.value" class="text-on-surface-muted">
      {{ t('common.loading') }}
    </p>
    <FormAlert
      v-else-if="explanation.isError.value"
      :message="errorMessage(t, explanation.error.value)"
    />
    <template v-else>
      <p class="font-medium" role="status" data-testid="explain-result">{{ summary }}</p>
      <ol class="flex flex-col border-l-2 border-border pl-4" data-testid="explain-steps">
        <li
          v-for="(step, index) in steps"
          :key="index"
          class="relative py-1.5"
          :class="index === decisive ? 'font-medium text-on-surface' : 'text-on-surface-muted'"
          :data-testid="index === decisive ? 'explain-decisive' : 'explain-step'"
        >
          <span
            class="absolute top-3 -left-[1.3rem] size-2.5 rounded-full"
            :class="index === decisive ? 'bg-primary' : 'bg-border-strong'"
            aria-hidden="true"
          />
          {{ stepSentence(t, step) }}
          <span
            v-if="index === decisive"
            class="ml-1 rounded-full bg-primary-soft px-2 py-0.5 text-xs text-on-primary-soft"
            >{{ t('permissions.explain.decisive') }}</span
          >
        </li>
      </ol>
    </template>
  </div>
</template>
