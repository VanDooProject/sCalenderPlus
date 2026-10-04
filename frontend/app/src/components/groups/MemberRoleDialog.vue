<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { UiButton, UiCheckbox, UiDialog, UiField, UiSelect, useToast } from '@scalenderplus/ui'
import type { GroupRole, MemberResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import { useCurrentGroup } from '@/composables/currentGroup'
import { useGroupChange } from '@/composables/groups'
import { call } from '@/lib/apiError'
import { groupErrorMessage } from '@/lib/groupErrors'
import { asRole, atLeast } from '@/lib/groupRoles'

const props = defineProps<{
  member: MemberResponse | null
  self: boolean
  /** Roles the actor may give (computed from the roles; the api decides). */
  roles: GroupRole[]
}>()
const open = defineModel<boolean>('open', { default: false })

const { t } = useI18n()
const toast = useToast()
const { id } = useCurrentGroup()
const run = useGroupChange(id)

const role = ref<GroupRole>('member')
const revokeShares = ref(true)
const pending = ref(false)
const error = ref<string | null>(null)

const current = computed(() => (props.member ? asRole(props.member.role) : 'viewer'))
const demotion = computed(() => !atLeast(role.value, current.value))
const name = computed(() => props.member?.displayName ?? '')
const options = computed(() =>
  props.roles.map((value) => ({
    value,
    label: t(`groups.roles.${value}`),
  })),
)

watch(open, (isOpen) => {
  if (!isOpen) return
  role.value = current.value
  revokeShares.value = true
  error.value = null
})

async function submit() {
  const member = props.member
  if (!member) return
  pending.value = true
  error.value = null
  try {
    const updated = await run(async () => {
      const { data } = await call(
        api.PATCH('/api/v1/groups/{id}/members/{userId}', {
          params: {
            path: { id: id.value, userId: member.userId },
            query: demotion.value && !revokeShares.value ? { revokeEventShares: false } : {},
            header: { 'If-Match': member.etag },
          },
          body: { role: role.value },
        }),
      )
      return data
    })
    open.value = false
    toast.success(
      t('groups.roleDialog.changed', {
        name: updated.displayName,
        role: t(`groups.roles.${asRole(updated.role)}`),
      }),
    )
  } catch (e) {
    error.value = groupErrorMessage(t, e, { kind: 'role', name: name.value, self: props.self })
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="self ? t('groups.roleDialog.selfTitle') : t('groups.roleDialog.title', { name })"
    :description="
      self ? t('groups.roleDialog.selfDescription') : t('groups.roleDialog.description')
    "
    :close-label="t('common.close')"
    testid="role-dialog"
  >
    <form id="role-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <FormAlert :message="error" />
      <UiField :label="t('groups.roleDialog.role')" :hint="t(`groups.roleHints.${role}`)">
        <UiSelect v-model="role" :options="options" testid="role-select" />
      </UiField>
      <UiCheckbox
        v-if="demotion"
        v-model="revokeShares"
        :label="t('groups.revokeShares.label')"
        :description="
          self ? t('groups.revokeShares.selfHint') : t('groups.revokeShares.demoteHint', { name })
        "
        data-testid="role-revoke-shares"
      />
    </form>
    <template #footer>
      <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
      <UiButton
        type="submit"
        form="role-form"
        :disabled="role === current"
        :loading="pending"
        data-testid="role-submit"
      >
        {{ t('groups.roleDialog.submit') }}
      </UiButton>
    </template>
  </UiDialog>
</template>
