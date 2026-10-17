<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft, Lock, SearchX, Snowflake, Trash2 } from '@lucide/vue'
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
import type { CalendarResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import EmptyState from '@/components/EmptyState.vue'
import FormAlert from '@/components/FormAlert.vue'
import SettingsCard from '@/components/SettingsCard.vue'
import TimeZonePicker from '@/components/TimeZonePicker.vue'
import CalendarColorPicker from '@/components/calendar/CalendarColorPicker.vue'
import GrantsCard from '@/components/calendar/GrantsCard.vue'
import IcsImportCard from '@/components/calendar/IcsImportCard.vue'
import LevelHelp from '@/components/calendar/LevelHelp.vue'
import CalendarAppsCard from '@/components/feeds/CalendarAppsCard.vue'
import ShareLinksCard from '@/components/feeds/ShareLinksCard.vue'
import { calendarsQueryKey } from '@/composables/calendars'
import { calendarKeys, useCalendar, useCalendarChange } from '@/composables/calendarSettings'
import { useForm, useValidators } from '@/composables/form'
import { call } from '@/lib/apiError'
import { fallbackColor } from '@/lib/calendarColors'
import {
  asLevel,
  calendarErrorMessage,
  canDeleteCalendar,
  canManageCalendar,
  isLowering,
  levelAtLeast,
  roleDefaultLevels,
} from '@/lib/calendarLevels'
import { isApiError } from '@/lib/errorMessages'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const toast = useToast()
const queryClient = useQueryClient()
const rules = useValidators()

const id = computed(() => String(route.params.calendarId ?? ''))
const query = useCalendar(id)
const calendar = computed<CalendarResponse | null>(() => query.data.value?.calendar ?? null)
const etag = computed(() => query.data.value?.etag ?? null)
const run = useCalendarChange(id)
const notFound = computed(() => isApiError(query.error.value, 'not_found'))

const myLevel = computed(() => asLevel(calendar.value?.myLevel))
const manageable = computed(() => canManageCalendar(myLevel.value))
const frozen = computed(() => calendar.value?.frozen ?? false)
/** Edits that the api refuses (`409 calendar_frozen`) are disabled; cleanup stays possible. */
const editable = computed(() => manageable.value && !frozen.value)
const isGroupCalendar = computed(() => calendar.value?.owner.type === 'group')

/** `If-Match` of the next PATCH/DELETE: always the latest loaded version. */
const ifMatch = () => etag.value ?? '*'

// General.
type General = { name: string; description: string; color: string; defaultTimeZone: string }
const fromCalendar = (): General => ({
  name: calendar.value?.name ?? '',
  description: calendar.value?.description ?? '',
  color: calendar.value?.color ?? fallbackColor,
  defaultTimeZone: calendar.value?.defaultTimeZone ?? 'UTC',
})
const generalRef = ref<HTMLFormElement>()
const general = useForm<General>(fromCalendar(), {
  formRef: generalRef,
  validate: (v) => ({ name: rules.required(v.name) }),
  errorMessage: (error) => calendarErrorMessage(t, error),
})
/** What the form was last reset to: edits are the differences from it. */
const generalBase = ref<General>(fromCalendar())
const generalDirty = computed(() =>
  (Object.keys(generalBase.value) as (keyof General)[]).some(
    (k) => generalBase.value[k] !== general.values[k],
  ),
)
function resetGeneral(values: General = fromCalendar()) {
  generalBase.value = { ...values }
  general.reset(values)
}

async function saveGeneral() {
  await general.submit(async (v) => {
    const saved = fromCalendar()
    const patch = Object.fromEntries(
      (Object.keys(v) as (keyof General)[])
        .filter((key) => saved[key] !== v[key])
        .map((key) => [key, key === 'name' || key === 'description' ? v[key].trim() : v[key]]),
    )
    await run(() =>
      call(
        api.PATCH('/api/v1/calendars/{id}', {
          params: { path: { id: id.value }, header: { 'If-Match': ifMatch() } },
          body: patch,
        }),
      ),
    )
    resetGeneral({ ...v, name: v.name.trim(), description: v.description.trim() })
    toast.success(t('calendarSettings.saved'))
  })
}

// Permissions.
type RoleKey = 'admin' | 'member' | 'viewer'
const roleKeys: RoleKey[] = ['admin', 'member', 'viewer']
type Perms = {
  creatorsManageOwnEvents: boolean
  creatorsMayShareExternally: boolean
  admin: string
  member: string
  viewer: string
}
const fromPerms = (): Perms => ({
  creatorsManageOwnEvents: calendar.value?.creatorsManageOwnEvents ?? true,
  creatorsMayShareExternally: calendar.value?.creatorsMayShareExternally ?? false,
  admin: calendar.value?.groupRoleDefaults?.admin ?? 'manage',
  member: calendar.value?.groupRoleDefaults?.member ?? 'contribute',
  viewer: calendar.value?.groupRoleDefaults?.viewer ?? 'read',
})
const perms = useForm<Perms>(fromPerms(), {
  errorMessage: (error) => calendarErrorMessage(t, error),
})
const permsBase = ref<Perms>(fromPerms())
const permsDirty = computed(() =>
  (Object.keys(permsBase.value) as (keyof Perms)[]).some(
    (k) => permsBase.value[k] !== perms.values[k],
  ),
)
function resetPerms(values: Perms = fromPerms()) {
  permsBase.value = { ...values }
  perms.reset(values)
}
/** Lowering a role default may take event shares away: ask (default: revoke them). */
const lowersDefaults = computed(
  () =>
    isGroupCalendar.value &&
    roleKeys.some((role) => isLowering(fromPerms()[role], perms.values[role])),
)
const revokeOnLower = ref(true)
const levelOptions = computed(() =>
  roleDefaultLevels(myLevel.value).map((level) => ({
    value: level as string,
    label: t(`calendarSettings.levels.${level}.label`),
  })),
)

async function savePerms() {
  await perms.submit(async (v) => {
    const saved = fromPerms()
    const roleDefaults = Object.fromEntries(
      roleKeys.filter((role) => saved[role] !== v[role]).map((role) => [role, v[role]]),
    )
    const body = {
      ...(saved.creatorsManageOwnEvents !== v.creatorsManageOwnEvents
        ? { creatorsManageOwnEvents: v.creatorsManageOwnEvents }
        : {}),
      ...(saved.creatorsMayShareExternally !== v.creatorsMayShareExternally
        ? { creatorsMayShareExternally: v.creatorsMayShareExternally }
        : {}),
      ...(Object.keys(roleDefaults).length > 0 ? { groupRoleDefaults: roleDefaults } : {}),
    }
    await run(() =>
      call(
        api.PATCH('/api/v1/calendars/{id}', {
          params: {
            path: { id: id.value },
            query: lowersDefaults.value && !revokeOnLower.value ? { revokeEventShares: false } : {},
            header: { 'If-Match': ifMatch() },
          },
          body,
        }),
      ),
    )
    resetPerms({ ...v })
    revokeOnLower.value = true
    toast.success(t('calendarSettings.saved'))
  })
}

// Follow the stored calendar while there are no unsaved edits (e.g. after a reload on 412).
watch(calendar, () => {
  if (!generalDirty.value && !general.pending.value) resetGeneral()
  if (!permsDirty.value && !perms.pending.value) resetPerms()
})

// Deleting.
const deleteOpen = ref(false)
const deleteConfirm = ref('')
const deletePending = ref(false)
const deleteError = ref<string | null>(null)
watch(deleteOpen, (open) => {
  if (!open) return
  deleteConfirm.value = ''
  deleteError.value = null
})

async function deleteCalendar() {
  const name = calendar.value?.name ?? ''
  const calendarId = id.value
  deletePending.value = true
  deleteError.value = null
  try {
    await call(
      api.DELETE('/api/v1/calendars/{id}', {
        params: { path: { id: calendarId }, header: { 'If-Match': ifMatch() } },
      }),
    )
    deleteOpen.value = false
    await router.push({ name: 'calendar' })
    queryClient.removeQueries({ queryKey: calendarKeys.detail(calendarId) })
    await queryClient.invalidateQueries({ queryKey: calendarsQueryKey, exact: true })
    toast.success(t('calendarSettings.danger.deleted', { name }))
  } catch (error) {
    if (isApiError(error, 'precondition_failed')) {
      await queryClient.invalidateQueries({ queryKey: calendarKeys.detail(calendarId) })
    }
    deleteError.value = calendarErrorMessage(t, error)
  } finally {
    deletePending.value = false
  }
}
</script>

<template>
  <div class="mx-auto flex max-w-4xl flex-col gap-6" data-testid="calendar-settings">
    <RouterLink
      :to="{ name: 'calendar' }"
      class="inline-flex w-fit items-center gap-1.5 rounded-md text-sm font-medium text-on-surface-muted hover:text-on-surface focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
      data-testid="calendar-settings-back"
    >
      <ArrowLeft class="size-4" aria-hidden="true" />{{ t('calendarSettings.back') }}
    </RouterLink>

    <EmptyState
      v-if="notFound"
      :title="t('calendarSettings.notFoundTitle')"
      :description="t('calendarSettings.notFoundText')"
      data-testid="calendar-settings-not-found"
    >
      <template #icon><SearchX aria-hidden="true" /></template>
    </EmptyState>
    <UiAlert v-else-if="query.isError.value" variant="danger" role="alert">
      <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <span>{{ calendarErrorMessage(t, query.error.value) }}</span>
        <UiButton variant="secondary" size="sm" @click="query.refetch()">
          {{ t('common.retry') }}
        </UiButton>
      </div>
    </UiAlert>
    <div v-else-if="!calendar" class="flex flex-col gap-4" aria-hidden="true">
      <div class="h-8 w-64 animate-pulse rounded bg-surface-hover" />
      <div class="h-48 animate-pulse rounded-2xl bg-surface-hover" />
    </div>
    <template v-else>
      <header class="flex flex-col gap-1">
        <div class="flex flex-wrap items-center gap-3">
          <span
            class="size-4 shrink-0 rounded-[0.3rem]"
            :style="{ backgroundColor: calendar.color ?? fallbackColor }"
            aria-hidden="true"
          />
          <h1
            class="text-2xl font-semibold tracking-tight break-words"
            data-testid="calendar-settings-title"
          >
            {{ t('calendarSettings.title', { name: calendar.name }) }}
          </h1>
        </div>
        <p class="text-sm text-on-surface-muted" data-testid="calendar-my-level">
          {{
            t('calendarSettings.yourLevel', {
              level: t(`calendarSettings.levels.${myLevel}.label`),
            })
          }}
          – {{ t(`calendarSettings.levels.${myLevel}.hint`) }}
        </p>
      </header>

      <UiAlert v-if="frozen" variant="warning" data-testid="calendar-frozen">
        <template #icon><Snowflake aria-hidden="true" /></template>
        {{ t('calendarSettings.frozen') }}
      </UiAlert>
      <UiAlert v-if="!manageable" data-testid="calendar-settings-readonly">
        <template #icon><Lock aria-hidden="true" /></template>
        {{ t('calendarSettings.readOnly') }}
      </UiAlert>

      <form
        ref="generalRef"
        novalidate
        data-testid="calendar-general-form"
        @submit.prevent="saveGeneral"
      >
        <SettingsCard
          :title="t('calendarSettings.general.title')"
          :description="t('calendarSettings.general.description')"
        >
          <div class="flex flex-col gap-5">
            <FormAlert :message="general.formError.value" />
            <UiField
              :label="t('calendarSettings.general.name')"
              :errors="general.errors('name')"
              required
            >
              <UiInput
                v-model="general.values.name"
                name="name"
                maxlength="100"
                autocomplete="off"
                :disabled="!editable"
                data-testid="calendar-settings-name"
              />
            </UiField>
            <UiField
              :label="t('calendarSettings.general.descriptionLabel')"
              :errors="general.errors('description')"
            >
              <UiInput
                v-model="general.values.description"
                name="description"
                maxlength="1000"
                autocomplete="off"
                :disabled="!editable"
                data-testid="calendar-settings-description"
              />
            </UiField>
            <CalendarColorPicker
              v-model="general.values.color"
              :label="t('calendarSettings.general.color')"
              :disabled="!editable"
            />
            <UiField
              :label="t('calendarSettings.general.timeZone')"
              :hint="t('calendarSettings.general.timeZoneHint')"
              :errors="general.errors('defaultTimeZone')"
            >
              <TimeZonePicker v-if="editable" v-model="general.values.defaultTimeZone" />
              <UiInput
                v-else
                :model-value="general.values.defaultTimeZone.replaceAll('_', ' ')"
                disabled
              />
            </UiField>
          </div>
          <template v-if="editable" #footer>
            <UiButton
              variant="ghost"
              :disabled="!generalDirty || general.pending.value"
              @click="resetGeneral()"
            >
              {{ t('common.discard') }}
            </UiButton>
            <UiButton
              type="submit"
              :disabled="!generalDirty"
              :loading="general.pending.value"
              data-testid="calendar-general-save"
            >
              {{ t('common.save') }}
            </UiButton>
          </template>
        </SettingsCard>
      </form>

      <form
        v-if="manageable"
        novalidate
        data-testid="calendar-permissions-form"
        @submit.prevent="savePerms"
      >
        <SettingsCard
          :title="t('calendarSettings.permissions.title')"
          :description="t('calendarSettings.permissions.description')"
        >
          <div class="flex flex-col gap-5">
            <FormAlert :message="perms.formError.value" />
            <UiCheckbox
              v-model="perms.values.creatorsManageOwnEvents"
              :label="t('calendarSettings.permissions.creatorsManage')"
              :description="t('calendarSettings.permissions.creatorsManageHint')"
              :disabled="!editable"
              data-testid="creators-manage"
            />
            <UiCheckbox
              v-model="perms.values.creatorsMayShareExternally"
              :label="t('calendarSettings.permissions.creatorsShare')"
              :description="t('calendarSettings.permissions.creatorsShareHint')"
              :disabled="!editable"
              data-testid="creators-share"
            />
            <fieldset
              v-if="isGroupCalendar"
              class="flex flex-col gap-4"
              data-testid="role-defaults"
            >
              <legend class="mb-1 text-sm font-medium">
                {{ t('calendarSettings.permissions.roleDefaults') }}
              </legend>
              <p class="-mt-2 text-sm text-on-surface-muted">
                {{ t('calendarSettings.permissions.roleDefaultsHint') }}
              </p>
              <UiField
                v-for="role in roleKeys"
                :key="role"
                :label="t(`groups.roles.${role}`)"
                :hint="t(`calendarSettings.levels.${asLevel(perms.values[role])}.hint`)"
              >
                <UiSelect
                  v-model="perms.values[role]"
                  :options="levelOptions"
                  :disabled="!editable"
                  :testid="`role-default-${role}`"
                />
              </UiField>
              <UiCheckbox
                v-if="lowersDefaults"
                v-model="revokeOnLower"
                :label="t('calendarSettings.revokeShares.label')"
                :description="t('calendarSettings.revokeShares.roleDefaultsHint')"
                data-testid="role-defaults-revoke"
              />
            </fieldset>
            <LevelHelp />
          </div>
          <template v-if="editable" #footer>
            <UiButton
              variant="ghost"
              :disabled="!permsDirty || perms.pending.value"
              @click="resetPerms()"
            >
              {{ t('common.discard') }}
            </UiButton>
            <UiButton
              type="submit"
              :disabled="!permsDirty"
              :loading="perms.pending.value"
              data-testid="calendar-permissions-save"
            >
              {{ t('common.save') }}
            </UiButton>
          </template>
        </SettingsCard>
      </form>

      <GrantsCard v-if="manageable" :calendar="calendar" :my-level="myLevel" :run="run" />
      <ShareLinksCard v-if="manageable" :calendar-id="calendar.id" :frozen="frozen" />
      <CalendarAppsCard :calendar-id="calendar.id" :calendar-name="calendar.name" />

      <IcsImportCard v-if="levelAtLeast(myLevel, 'contribute')" :calendar="calendar" />

      <SettingsCard
        v-if="canDeleteCalendar(myLevel)"
        :title="t('calendarSettings.danger.title')"
        :description="t('calendarSettings.danger.description')"
      >
        <UiButton variant="danger" data-testid="calendar-delete" @click="deleteOpen = true">
          <Trash2 class="size-4" aria-hidden="true" />{{ t('calendarSettings.danger.delete') }}
        </UiButton>
      </SettingsCard>

      <UiDialog
        v-model:open="deleteOpen"
        :title="t('calendarSettings.danger.deleteTitle', { name: calendar.name })"
        :description="t('calendarSettings.danger.deleteConfirm')"
        :close-label="t('common.close')"
        testid="delete-calendar-dialog"
      >
        <form
          id="delete-calendar-form"
          class="flex flex-col gap-5"
          novalidate
          @submit.prevent="deleteCalendar"
        >
          <FormAlert :message="deleteError" />
          <UiField :label="t('calendarSettings.danger.deleteConfirmLabel')">
            <UiInput
              v-model="deleteConfirm"
              autocomplete="off"
              data-testid="delete-calendar-confirm"
            />
          </UiField>
        </form>
        <template #footer>
          <UiButton variant="ghost" @click="deleteOpen = false">{{ t('common.cancel') }}</UiButton>
          <UiButton
            type="submit"
            form="delete-calendar-form"
            variant="danger"
            :disabled="deleteConfirm.trim() !== calendar.name.trim()"
            :loading="deletePending"
            data-testid="delete-calendar-submit"
          >
            {{ t('calendarSettings.danger.delete') }}
          </UiButton>
        </template>
      </UiDialog>
    </template>
  </div>
</template>
