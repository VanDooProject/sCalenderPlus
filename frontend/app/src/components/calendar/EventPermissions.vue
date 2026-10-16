<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQueryClient } from '@tanstack/vue-query'
import { EyeOff, Globe, Link2, Plus, ShieldCheck, Trash2, User, Users } from '@lucide/vue'
import { UiButton, UiField, UiSelect, useToast } from '@scalenderplus/ui'
import type { EventResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import { usePrincipalCandidates } from '@/composables/eventPermissions'
import { call } from '@/lib/apiError'
import { errorMessage, isApiError } from '@/lib/errorMessages'
import { groupRoles } from '@/lib/groupRoles'
import {
  applyPreset,
  levelLabel,
  overrideLevels,
  principalKey,
  principalLabel,
  requestFromRows,
  rowsFromResponse,
  sortRows,
  violationMessage,
  violationsByRow,
  violationsOf,
  type OverrideRow,
  type Preset,
} from '@/lib/eventPermissions'

/**
 * The event's permission overrides (`manage` only, api.md "Event overrides"): change, remove and
 * add entries, presets, then one atomic `PUT` with `If-Match`. Refusals name the offending rows.
 */
const props = defineProps<{ event: EventResponse }>()

const { t } = useI18n()
const toast = useToast()
const queryClient = useQueryClient()

const rows = ref<OverrideRow[]>([])
const savedRows = ref<OverrideRow[]>([])
const etag = ref<string | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const formError = ref<string | null>(null)
const rowErrors = ref<Record<string, string[]>>({})
const saving = ref(false)

const dirty = computed(
  () =>
    JSON.stringify(requestFromRows(rows.value)) !==
    JSON.stringify(requestFromRows(savedRows.value)),
)

async function load(keepEdits = false) {
  loadError.value = null
  try {
    const { data } = await call(
      api.GET('/api/v1/events/{id}/overrides', { params: { path: { id: props.event.id } } }),
    )
    etag.value = data.etag
    const fresh = rowsFromResponse(data.items)
    savedRows.value = fresh
    if (!keepEdits) rows.value = fresh
  } catch (error) {
    loadError.value = errorMessage(t, error)
  } finally {
    loading.value = false
  }
}
onMounted(() => load())
watch(
  () => props.event.id,
  () => {
    loading.value = true
    rowErrors.value = {}
    formError.value = null
    void load()
  },
)

const levelOptions = computed(() =>
  overrideLevels.map((l) => ({ value: l as string, label: levelLabel(t, l) })),
)

function setLevel(row: OverrideRow, level: string | undefined) {
  if (!level) return
  rows.value = rows.value.map((r) => (r === row ? { ...r, level } : r))
}

function remove(row: OverrideRow) {
  rows.value = rows.value.filter((r) => r !== row)
}

function preset(kind: Preset) {
  rows.value = applyPreset(rows.value, kind)
}

const iconOf = (type: string) =>
  type === 'group' ? Users : type === 'everyone' ? Globe : type === 'anonymous' ? Link2 : User

// Adding: people from my groups, my groups (optionally from a role up), everyone in the calendar.
const { people, groups } = usePrincipalCandidates(true)
const addPrincipal = ref<string>()
const addMinRole = ref<string>('viewer')
const addLevel = ref<string>('read')

const candidates = computed(() => {
  const taken = new Set(rows.value.map((r) => principalKey(r.principal)))
  return [
    ...people.value
      .filter((p) => !taken.has(`user:${p.id}`))
      .map((p) => ({ value: `user:${p.id}`, label: p.name })),
    ...groups.value
      .filter((g) => !taken.has(`group:${g.id}`))
      .map((g) => ({
        value: `group:${g.id}`,
        label: t('permissions.add.groupOption', { group: g.name }),
      })),
    ...(taken.has('everyone')
      ? []
      : [{ value: 'everyone', label: t('permissions.principal.everyone') }]),
  ]
})
const isGroup = computed(() => addPrincipal.value?.startsWith('group:') ?? false)
const minRoleOptions = computed(() =>
  groupRoles.map((role) => ({
    value: role as string,
    label:
      role === 'viewer'
        ? t('calendarSettings.sharing.minRoleAll')
        : t('calendarSettings.sharing.minRoleAtLeast', { role: t(`groups.roles.${role}`) }),
  })),
)

function add() {
  const selected = addPrincipal.value
  if (!selected) return
  const [type, id] = selected.split(':') as [string, string | undefined]
  const name =
    type === 'user'
      ? (people.value.find((p) => p.id === id)?.name ?? null)
      : type === 'group'
        ? (groups.value.find((g) => g.id === id)?.name ?? null)
        : null
  rows.value = sortRows([
    ...rows.value,
    {
      principal: { type, id: id ?? null, minRole: type === 'group' ? addMinRole.value : null },
      name,
      level: type === 'everyone' && addLevel.value === 'edit' ? 'read' : addLevel.value,
    },
  ])
  addPrincipal.value = undefined
  addMinRole.value = 'viewer'
  addLevel.value = 'read'
}

async function save() {
  if (!etag.value) return
  saving.value = true
  formError.value = null
  rowErrors.value = {}
  try {
    const { data } = await call(
      api.PUT('/api/v1/events/{id}/overrides', {
        params: { path: { id: props.event.id }, header: { 'If-Match': etag.value } },
        body: requestFromRows(rows.value),
      }),
    )
    etag.value = data.etag
    rows.value = rowsFromResponse(data.items)
    savedRows.value = rows.value
    toast.success(t('permissions.saved'))
    await queryClient.invalidateQueries({ queryKey: ['events'] })
  } catch (error) {
    const violations = violationsOf(error)
    if (violations.length) {
      rowErrors.value = Object.fromEntries(
        Object.entries(violationsByRow(violations)).map(([key, reasons]) => [
          key,
          reasons.map((r) => violationMessage(t, r)),
        ]),
      )
      formError.value = isApiError(error, 'external_sharing_not_allowed')
        ? t('permissions.errors.external')
        : t('permissions.errors.invalid')
    } else if (isApiError(error, 'precondition_failed')) {
      // Someone changed the permissions meanwhile: take the new version, keep the edits.
      await load(true)
      formError.value = t('permissions.errors.changed')
    } else if (isApiError(error, 'plan_limit_reached')) {
      formError.value = t('permissions.errors.planLimit')
    } else if (isApiError(error, 'calendar_frozen')) {
      formError.value = t('permissions.errors.frozen')
    } else {
      formError.value = errorMessage(t, error)
    }
  } finally {
    saving.value = false
  }
}

function reset() {
  rows.value = savedRows.value
  rowErrors.value = {}
  formError.value = null
}
</script>

<template>
  <div class="flex flex-col gap-4 text-sm" data-testid="event-permissions">
    <p class="flex items-start gap-2 text-on-surface-muted">
      <ShieldCheck class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <span>{{ t('permissions.floorsHint') }}</span>
    </p>

    <p v-if="loading" class="text-on-surface-muted">{{ t('common.loading') }}</p>
    <FormAlert v-else-if="loadError" :message="loadError" />
    <template v-else>
      <FormAlert :message="formError" />

      <div class="flex flex-wrap gap-2" role="group" :aria-label="t('permissions.presets.label')">
        <UiButton
          size="sm"
          variant="secondary"
          data-testid="preset-private"
          @click="preset('private')"
        >
          <EyeOff class="size-4" aria-hidden="true" />{{ t('permissions.presets.private') }}
        </UiButton>
        <UiButton size="sm" variant="secondary" data-testid="preset-busy" @click="preset('busy')">
          {{ t('permissions.presets.busy') }}
        </UiButton>
      </div>

      <p v-if="rows.length === 0" class="text-on-surface-muted" data-testid="overrides-empty">
        {{ t('permissions.empty') }}
      </p>
      <ul v-else class="flex flex-col divide-y divide-border" data-testid="overrides">
        <li
          v-for="row in rows"
          :key="principalKey(row.principal)"
          class="flex flex-col gap-1 py-2"
          :data-testid="`override-${principalKey(row.principal)}`"
        >
          <div class="flex items-center gap-2">
            <component
              :is="iconOf(row.principal.type)"
              class="size-4 shrink-0 text-on-surface-muted"
              aria-hidden="true"
            />
            <span class="min-w-0 flex-1 truncate font-medium">{{
              principalLabel(t, row.principal, row.name)
            }}</span>
            <div class="w-36">
              <UiSelect
                :model-value="row.level"
                :options="levelOptions"
                :invalid="!!rowErrors[principalKey(row.principal)]"
                :aria-label="
                  t('permissions.levelOf', { name: principalLabel(t, row.principal, row.name) })
                "
                :testid="`override-level-${principalKey(row.principal)}`"
                @update:model-value="setLevel(row, $event)"
              />
            </div>
            <UiButton
              variant="ghost"
              size="icon"
              :aria-label="
                t('permissions.remove', { name: principalLabel(t, row.principal, row.name) })
              "
              :data-testid="`override-remove-${principalKey(row.principal)}`"
              @click="remove(row)"
            >
              <Trash2 class="size-4" aria-hidden="true" />
            </UiButton>
          </div>
          <p
            v-for="message in rowErrors[principalKey(row.principal)] ?? []"
            :key="message"
            class="pl-6 text-danger"
            data-testid="override-violation"
          >
            {{ message }}
          </p>
        </li>
      </ul>
      <p
        v-if="rows.some((r) => r.level === 'free_busy')"
        class="text-on-surface-muted"
        data-testid="overrides-busy-preview"
      >
        {{ t('permissions.busyPreview', { title: event.coverTitle || t('calendar.busy') }) }}
      </p>

      <fieldset class="flex flex-col gap-3 rounded-lg border border-border p-3">
        <legend class="px-1 font-medium">{{ t('permissions.add.title') }}</legend>
        <UiField :label="t('permissions.add.who')">
          <UiSelect
            v-model="addPrincipal"
            :options="candidates"
            :placeholder="t('permissions.add.whoPlaceholder')"
            testid="override-add-principal"
          />
        </UiField>
        <UiField v-if="isGroup" :label="t('calendarSettings.sharing.minRole')">
          <UiSelect v-model="addMinRole" :options="minRoleOptions" testid="override-add-min-role" />
        </UiField>
        <UiField :label="t('permissions.add.level')">
          <UiSelect v-model="addLevel" :options="levelOptions" testid="override-add-level" />
        </UiField>
        <div>
          <UiButton
            size="sm"
            variant="secondary"
            :disabled="!addPrincipal"
            data-testid="override-add"
            @click="add"
          >
            <Plus class="size-4" aria-hidden="true" />{{ t('permissions.add.submit') }}
          </UiButton>
        </div>
      </fieldset>

      <div class="flex justify-end gap-2">
        <UiButton variant="ghost" :disabled="!dirty || saving" @click="reset">
          {{ t('permissions.reset') }}
        </UiButton>
        <UiButton :disabled="!dirty" :loading="saving" data-testid="overrides-save" @click="save">
          {{ t('permissions.save') }}
        </UiButton>
      </div>
    </template>
  </div>
</template>
