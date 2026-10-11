<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQueries } from '@tanstack/vue-query'
import { Plus, Trash2, Users, User } from '@lucide/vue'
import { UiButton, UiCheckbox, UiDialog, UiField, UiSelect, useToast } from '@scalenderplus/ui'
import type { CalendarResponse, GrantResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import SettingsCard from '@/components/SettingsCard.vue'
import { useGrants } from '@/composables/calendarSettings'
import { membersQuery, useGroups } from '@/composables/groups'
import { useSession } from '@/composables/session'
import { call } from '@/lib/apiError'
import {
  asLevel,
  blockedByFrozen,
  calendarErrorMessage,
  grantableLevels,
  isLowering,
  type CalendarLevel,
} from '@/lib/calendarLevels'
import { groupRoles } from '@/lib/groupRoles'

/**
 * The calendar's grants (`manage`): change a level, remove, add for a member of one of my groups
 * or one of my groups. Lowering and removing ask whether individual event shares go too.
 */
const props = defineProps<{
  calendar: CalendarResponse
  myLevel: CalendarLevel
  run: <T>(change: () => Promise<T>) => Promise<T>
}>()

const { t } = useI18n()
const toast = useToast()
const { user } = useSession()
const calendarId = computed(() => props.calendar.id)
const grants = useGrants(calendarId, true)
const frozen = computed(() => props.calendar.frozen)

const levelLabel = (level: string) => t(`calendarSettings.levels.${asLevel(level)}.label`)
const allLevels = computed(() => grantableLevels(props.myLevel))

/** Levels offered for an existing grant: on a frozen calendar only the current one and lower. */
function optionsFor(grant: GrantResponse) {
  const levels = allLevels.value.filter((l) => !blockedByFrozen(frozen.value, grant.level, l))
  const withCurrent = levels.includes(asLevel(grant.level))
    ? levels
    : [asLevel(grant.level), ...levels]
  return withCurrent.map((l) => ({ value: l as string, label: levelLabel(l) }))
}

function principalLabel(grant: GrantResponse): string {
  if (grant.principal.type !== 'group') return grant.principalName
  const role = grant.principal.minRole ?? 'viewer'
  return role === 'viewer'
    ? t('calendarSettings.sharing.groupAll', { group: grant.principalName })
    : t('calendarSettings.sharing.groupMinRole', {
        group: grant.principalName,
        role: t(`groups.roles.${role}`),
      })
}

// Changing and removing: raising goes straight through, lowering and removing ask first.
const pendingGrant = ref<{ grant: GrantResponse; level: string | null } | null>(null)
const confirmOpen = ref(false)
const revoke = ref(true)
const confirmError = ref<string | null>(null)
const busy = ref(false)

watch(confirmOpen, (open) => {
  if (open) {
    revoke.value = true
    confirmError.value = null
  }
})

async function changeLevel(grant: GrantResponse, level: string | undefined) {
  if (!level || level === grant.level) return
  if (isLowering(grant.level, level)) {
    pendingGrant.value = { grant, level }
    confirmOpen.value = true
    return
  }
  busy.value = true
  try {
    await patchGrant(grant, level, true)
    toast.success(t('calendarSettings.sharing.changed', { name: grant.principalName }))
  } catch (error) {
    toast.show({ variant: 'error', title: calendarErrorMessage(t, error) })
  } finally {
    busy.value = false
  }
}

function askRemove(grant: GrantResponse) {
  pendingGrant.value = { grant, level: null }
  confirmOpen.value = true
}

function patchGrant(grant: GrantResponse, level: string, revokeShares: boolean) {
  return props.run(() =>
    call(
      api.PATCH('/api/v1/calendars/{id}/grants/{grantId}', {
        params: {
          path: { id: calendarId.value, grantId: grant.id },
          query: revokeShares ? {} : { revokeEventShares: false },
          header: { 'If-Match': grant.etag },
        },
        body: { level },
      }),
    ),
  )
}

async function confirm() {
  const pending = pendingGrant.value
  if (!pending) return
  busy.value = true
  confirmError.value = null
  try {
    if (pending.level === null) {
      await props.run(() =>
        call(
          api.DELETE('/api/v1/calendars/{id}/grants/{grantId}', {
            params: {
              path: { id: calendarId.value, grantId: pending.grant.id },
              query: revoke.value ? {} : { revokeEventShares: false },
              header: { 'If-Match': pending.grant.etag },
            },
          }),
        ),
      )
      toast.success(t('calendarSettings.sharing.removed', { name: pending.grant.principalName }))
    } else {
      await patchGrant(pending.grant, pending.level, revoke.value)
      toast.success(t('calendarSettings.sharing.changed', { name: pending.grant.principalName }))
    }
    confirmOpen.value = false
  } catch (error) {
    confirmError.value = calendarErrorMessage(t, error)
  } finally {
    busy.value = false
  }
}

// Adding: members of my groups and my groups (selectable principals, api.md "Grants").
const groups = useGroups()
const addOpen = ref(false)
const memberLists = useQueries({
  queries: computed(() =>
    addOpen.value
      ? (groups.data.value ?? []).map((g) => ({ ...membersQuery(g.id), retry: false }))
      : [],
  ),
})

const candidates = computed(() => {
  const granted = new Set(
    (grants.data.value ?? []).map((g) => `${g.principal.type}:${g.principal.id}`),
  )
  const owner = `${props.calendar.owner.type}:${props.calendar.owner.id}`
  const skip = (key: string) =>
    granted.has(key) || key === owner || key === `user:${user.value?.id}`
  const people = new Map<string, string>()
  for (const list of memberLists.value) {
    for (const member of list.data ?? []) {
      const key = `user:${member.userId}`
      if (!skip(key)) people.set(key, member.displayName)
    }
  }
  const myGroups = (groups.data.value ?? [])
    .map((g) => ({ key: `group:${g.id}`, name: g.name }))
    .filter((g) => !skip(g.key))
  return [
    ...[...people.entries()]
      .sort((a, b) => a[1].localeCompare(b[1]))
      .map(([value, name]) => ({ value, label: name })),
    ...myGroups.map((g) => ({
      value: g.key,
      label: t('calendarSettings.sharing.groupOption', { group: g.name }),
    })),
  ]
})

const addPrincipal = ref<string>()
const addLevel = ref<string>('read')
const addMinRole = ref<string>('viewer')
const addError = ref<string | null>(null)
const isGroupPrincipal = computed(() => addPrincipal.value?.startsWith('group:') ?? false)
const levelOptions = computed(() =>
  allLevels.value.map((l) => ({ value: l as string, label: levelLabel(l) })),
)
const minRoleOptions = computed(() =>
  groupRoles.map((role) => ({
    value: role as string,
    label:
      role === 'viewer'
        ? t('calendarSettings.sharing.minRoleAll')
        : t('calendarSettings.sharing.minRoleAtLeast', { role: t(`groups.roles.${role}`) }),
  })),
)

watch(addOpen, (open) => {
  if (!open) return
  addPrincipal.value = undefined
  addLevel.value = 'read'
  addMinRole.value = 'viewer'
  addError.value = null
})

async function add() {
  const selected = addPrincipal.value
  if (!selected) {
    addError.value = t('calendarSettings.sharing.pickSomeone')
    return
  }
  const [type, principalId] = selected.split(':') as [string, string]
  busy.value = true
  addError.value = null
  try {
    const { data } = await props.run(() =>
      call(
        api.POST('/api/v1/calendars/{id}/grants', {
          params: { path: { id: calendarId.value } },
          body: {
            principal: {
              type,
              id: principalId,
              ...(type === 'group' ? { minRole: addMinRole.value } : {}),
            },
            level: addLevel.value,
          },
        }),
      ),
    )
    addOpen.value = false
    toast.success(t('calendarSettings.sharing.added', { name: data.principalName }))
  } catch (error) {
    addError.value = calendarErrorMessage(t, error)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <SettingsCard
    :title="t('calendarSettings.sharing.title')"
    :description="t('calendarSettings.sharing.description')"
  >
    <div class="flex flex-col gap-4">
      <p v-if="grants.isPending.value" class="text-sm text-on-surface-muted">
        {{ t('common.loading') }}
      </p>
      <FormAlert
        v-else-if="grants.isError.value"
        :message="calendarErrorMessage(t, grants.error.value)"
      />
      <p
        v-else-if="(grants.data.value ?? []).length === 0"
        class="text-sm text-on-surface-muted"
        data-testid="grants-empty"
      >
        {{ t('calendarSettings.sharing.empty') }}
      </p>
      <ul v-else class="flex flex-col divide-y divide-border" data-testid="grants">
        <li
          v-for="grant in grants.data.value"
          :key="grant.id"
          class="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:gap-4"
          :data-testid="`grant-${grant.principal.id}`"
        >
          <span class="flex min-w-0 flex-1 items-center gap-2 text-sm">
            <Users
              v-if="grant.principal.type === 'group'"
              class="size-4 shrink-0 text-on-surface-muted"
              aria-hidden="true"
            />
            <User v-else class="size-4 shrink-0 text-on-surface-muted" aria-hidden="true" />
            <span class="truncate font-medium">{{ principalLabel(grant) }}</span>
          </span>
          <div class="flex items-center gap-2">
            <div class="w-44">
              <UiSelect
                :model-value="grant.level"
                :options="optionsFor(grant)"
                :aria-label="t('calendarSettings.sharing.levelOf', { name: grant.principalName })"
                :disabled="busy"
                :testid="`grant-level-${grant.principal.id}`"
                @update:model-value="changeLevel(grant, $event)"
              />
            </div>
            <UiButton
              variant="ghost"
              size="icon"
              :aria-label="t('calendarSettings.sharing.remove', { name: grant.principalName })"
              :data-testid="`grant-remove-${grant.principal.id}`"
              @click="askRemove(grant)"
            >
              <Trash2 class="size-4" aria-hidden="true" />
            </UiButton>
          </div>
        </li>
      </ul>
    </div>
    <template #footer>
      <p v-if="frozen" class="mr-auto text-sm text-on-surface-muted">
        {{ t('calendarSettings.sharing.frozenHint') }}
      </p>
      <UiButton
        variant="secondary"
        :disabled="frozen"
        data-testid="grant-add"
        @click="addOpen = true"
      >
        <Plus class="size-4" aria-hidden="true" />{{ t('calendarSettings.sharing.add') }}
      </UiButton>
    </template>
  </SettingsCard>

  <UiDialog
    v-model:open="addOpen"
    :title="t('calendarSettings.sharing.addTitle')"
    :description="t('calendarSettings.sharing.addDescription')"
    :close-label="t('common.close')"
    testid="add-grant-dialog"
  >
    <form id="add-grant-form" class="flex flex-col gap-5" novalidate @submit.prevent="add">
      <FormAlert :message="addError" />
      <UiField :label="t('calendarSettings.sharing.who')">
        <UiSelect
          v-model="addPrincipal"
          :options="candidates"
          :placeholder="t('calendarSettings.sharing.whoPlaceholder')"
          testid="grant-principal"
        />
      </UiField>
      <UiField
        v-if="isGroupPrincipal"
        :label="t('calendarSettings.sharing.minRole')"
        :hint="t('calendarSettings.sharing.minRoleHint')"
      >
        <UiSelect v-model="addMinRole" :options="minRoleOptions" testid="grant-min-role" />
      </UiField>
      <UiField
        :label="t('calendarSettings.sharing.level')"
        :hint="t(`calendarSettings.levels.${asLevel(addLevel)}.hint`)"
      >
        <UiSelect v-model="addLevel" :options="levelOptions" testid="grant-level" />
      </UiField>
    </form>
    <template #footer>
      <UiButton variant="ghost" @click="addOpen = false">{{ t('common.cancel') }}</UiButton>
      <UiButton type="submit" form="add-grant-form" :loading="busy" data-testid="grant-add-submit">
        {{ t('calendarSettings.sharing.addSubmit') }}
      </UiButton>
    </template>
  </UiDialog>

  <UiDialog
    v-model:open="confirmOpen"
    :title="
      pendingGrant?.level === null
        ? t('calendarSettings.sharing.removeTitle', { name: pendingGrant?.grant.principalName })
        : t('calendarSettings.sharing.lowerTitle', {
            name: pendingGrant?.grant.principalName,
            level: levelLabel(pendingGrant?.level ?? ''),
          })
    "
    :close-label="t('common.close')"
    testid="grant-confirm-dialog"
  >
    <form id="grant-confirm-form" class="flex flex-col gap-5" novalidate @submit.prevent="confirm">
      <FormAlert :message="confirmError" />
      <UiCheckbox
        v-model="revoke"
        :label="t('calendarSettings.revokeShares.label')"
        :description="
          t('calendarSettings.revokeShares.grantHint', { name: pendingGrant?.grant.principalName })
        "
        data-testid="grant-revoke"
      />
    </form>
    <template #footer>
      <UiButton variant="ghost" @click="confirmOpen = false">{{ t('common.cancel') }}</UiButton>
      <UiButton
        type="submit"
        form="grant-confirm-form"
        :variant="pendingGrant?.level === null ? 'danger' : 'primary'"
        :loading="busy"
        data-testid="grant-confirm-submit"
      >
        {{
          pendingGrant?.level === null
            ? t('calendarSettings.sharing.removeSubmit')
            : t('calendarSettings.sharing.lowerSubmit')
        }}
      </UiButton>
    </template>
  </UiDialog>
</template>
