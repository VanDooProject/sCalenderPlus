<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { CreditCard, LogOut, Trash2 } from '@lucide/vue'
import {
  UiAlert,
  UiButton,
  UiDialog,
  UiField,
  UiInput,
  UiSelect,
  useToast,
} from '@scalenderplus/ui'
import type { MemberResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import SettingsCard from '@/components/SettingsCard.vue'
import RemoveMemberDialog from '@/components/groups/RemoveMemberDialog.vue'
import { useCurrentGroup } from '@/composables/currentGroup'
import { forgetGroup, useGroupChange, useMembers } from '@/composables/groups'
import { useForm, useValidators } from '@/composables/form'
import { useSession } from '@/composables/session'
import { call } from '@/lib/apiError'
import { groupErrorMessage } from '@/lib/groupErrors'
import { canDeleteGroup, canEditGroup } from '@/lib/groupRoles'

const { t } = useI18n()
const toast = useToast()
const router = useRouter()
const queryClient = useQueryClient()
const rules = useValidators()
const { user } = useSession()
const { id, group, etag, myRole } = useCurrentGroup()
const run = useGroupChange(id)
const members = useMembers(id)

const editable = computed(() => canEditGroup(myRole.value))
type Details = { name: string; description: string; memberListVisibility: string }
const fromGroup = (): Details => ({
  name: group.value?.name ?? '',
  description: group.value?.description ?? '',
  memberListVisibility: group.value?.memberListVisibility ?? 'all_members',
})

const formRef = ref<HTMLFormElement>()
const form = useForm<Details>(fromGroup(), {
  formRef,
  validate: (v) => ({ name: rules.required(v.name) }),
  errorMessage: (error) => groupErrorMessage(t, error, { kind: 'other' }),
})
const dirty = computed(() => {
  const saved = fromGroup()
  return (Object.keys(saved) as (keyof Details)[]).some((key) => saved[key] !== form.values[key])
})
// Follow the stored group while there are no unsaved edits (e.g. after a reload on 412).
watch(group, () => {
  if (!dirty.value && !form.pending.value) form.reset(fromGroup())
})

const visibilityOptions = computed(() => [
  { value: 'all_members', label: t('groups.settings.visibilityAll') },
  { value: 'members_and_above', label: t('groups.settings.visibilityMembers') },
])

async function save() {
  await form.submit(async (v) => {
    const saved = fromGroup()
    const patch = Object.fromEntries(
      (Object.keys(v) as (keyof Details)[])
        .filter((key) => saved[key] !== v[key])
        .map((key) => [key, key === 'memberListVisibility' ? v[key] : v[key].trim()]),
    )
    await run(() =>
      call(
        api.PATCH('/api/v1/groups/{id}', {
          params: { path: { id: id.value }, header: { 'If-Match': etag.value ?? '*' } },
          body: patch,
        }),
      ),
    )
    form.reset({ ...v, name: v.name.trim(), description: v.description.trim() })
    toast.success(t('groups.settings.saved'))
  })
}

const billingOwnerName = computed(
  () =>
    (members.data.value ?? []).find((m) => m.userId === group.value?.billingOwnerId)?.displayName ??
    null,
)

// Leaving: one's own membership from the list (its ETag), or `*` when the list is hidden.
const leaveOpen = ref(false)
const myMembership = computed<MemberResponse | null>(() => {
  const me = user.value
  if (!me) return null
  return (
    (members.data.value ?? []).find((m) => m.userId === me.id) ?? {
      userId: me.id,
      displayName: me.displayName,
      email: me.email,
      role: myRole.value,
      isBillingOwner: group.value?.billingOwnerId === me.id,
      joinedAt: '',
      etag: '*',
    }
  )
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

async function deleteGroup() {
  const name = group.value?.name ?? ''
  deletePending.value = true
  deleteError.value = null
  const groupId = id.value
  try {
    await run(() =>
      call(
        api.DELETE('/api/v1/groups/{id}', {
          params: { path: { id: groupId }, header: { 'If-Match': etag.value ?? '*' } },
        }),
      ),
    )
    deleteOpen.value = false
    await router.push({ name: 'groups' })
    await forgetGroup(queryClient, groupId)
    toast.success(t('groups.settings.deleted', { group: name }))
  } catch (error) {
    deleteError.value = groupErrorMessage(t, error, { kind: 'other' })
  } finally {
    deletePending.value = false
  }
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <form ref="formRef" novalidate data-testid="group-settings-form" @submit.prevent="save">
      <SettingsCard
        :title="t('groups.settings.title')"
        :description="t('groups.settings.description')"
      >
        <div class="flex flex-col gap-5">
          <UiAlert v-if="!editable" data-testid="group-settings-readonly">
            {{ t('groups.settings.readOnly') }}
          </UiAlert>
          <FormAlert :message="form.formError.value" />
          <UiField :label="t('groups.createDialog.name')" :errors="form.errors('name')" required>
            <UiInput
              v-model="form.values.name"
              name="name"
              maxlength="100"
              autocomplete="off"
              :disabled="!editable"
              data-testid="settings-group-name"
            />
          </UiField>
          <UiField
            :label="t('groups.createDialog.descriptionLabel')"
            :hint="t('groups.createDialog.descriptionHint')"
            :errors="form.errors('description')"
          >
            <UiInput
              v-model="form.values.description"
              name="description"
              maxlength="1000"
              autocomplete="off"
              :disabled="!editable"
              data-testid="settings-group-description"
            />
          </UiField>
          <UiField
            :label="t('groups.settings.visibility')"
            :errors="form.errors('memberListVisibility')"
          >
            <UiSelect
              v-model="form.values.memberListVisibility"
              :options="visibilityOptions"
              :disabled="!editable"
              testid="settings-group-visibility"
            />
          </UiField>
        </div>
        <template v-if="editable" #footer>
          <UiButton
            variant="ghost"
            :disabled="!dirty || form.pending.value"
            @click="form.reset(fromGroup())"
          >
            {{ t('common.discard') }}
          </UiButton>
          <UiButton
            type="submit"
            :disabled="!dirty"
            :loading="form.pending.value"
            data-testid="settings-group-save"
          >
            {{ t('common.save') }}
          </UiButton>
        </template>
      </SettingsCard>
    </form>

    <SettingsCard
      :title="t('groups.settings.billingTitle')"
      :description="t('groups.settings.billingText')"
    >
      <div class="flex flex-col gap-2 text-sm">
        <p v-if="billingOwnerName" class="flex items-center gap-2" data-testid="billing-owner-name">
          <CreditCard class="size-4 text-on-surface-muted" aria-hidden="true" />
          {{ t('groups.settings.billingCurrent', { name: billingOwnerName }) }}
        </p>
        <p v-if="myRole === 'owner'" class="text-on-surface-muted">
          {{ t('groups.settings.billingHowTo') }}
        </p>
      </div>
    </SettingsCard>

    <SettingsCard
      :title="t('groups.settings.leaveTitle')"
      :description="t('groups.settings.leaveText')"
    >
      <UiButton variant="secondary" data-testid="settings-leave" @click="leaveOpen = true">
        <LogOut class="size-4" aria-hidden="true" />{{ t('groups.members.leave') }}
      </UiButton>
    </SettingsCard>

    <SettingsCard
      v-if="canDeleteGroup(myRole)"
      :title="t('groups.settings.dangerTitle')"
      :description="t('groups.settings.dangerText')"
    >
      <UiButton variant="danger" data-testid="settings-delete" @click="deleteOpen = true">
        <Trash2 class="size-4" aria-hidden="true" />{{ t('groups.settings.delete') }}
      </UiButton>
    </SettingsCard>

    <RemoveMemberDialog v-model:open="leaveOpen" :member="myMembership" :self="true" />

    <UiDialog
      v-model:open="deleteOpen"
      :title="t('groups.settings.deleteTitle', { group: group?.name ?? '' })"
      :description="t('groups.settings.deleteConfirm')"
      :close-label="t('common.close')"
      testid="delete-group-dialog"
    >
      <form
        id="delete-group-form"
        class="flex flex-col gap-5"
        novalidate
        @submit.prevent="deleteGroup"
      >
        <FormAlert :message="deleteError" />
        <UiField :label="t('groups.settings.deleteConfirmLabel')">
          <UiInput v-model="deleteConfirm" autocomplete="off" data-testid="delete-group-confirm" />
        </UiField>
      </form>
      <template #footer>
        <UiButton variant="ghost" @click="deleteOpen = false">{{ t('common.cancel') }}</UiButton>
        <UiButton
          type="submit"
          form="delete-group-form"
          variant="danger"
          :disabled="deleteConfirm.trim() !== (group?.name ?? '').trim()"
          :loading="deletePending"
          data-testid="delete-group-submit"
        >
          {{ t('groups.settings.delete') }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>
