<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { UiButton, UiCheckbox, UiDialog, useToast } from '@scalenderplus/ui'
import type { MemberResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import { useCurrentGroup } from '@/composables/currentGroup'
import { forgetGroup, useGroupChange } from '@/composables/groups'
import { call } from '@/lib/apiError'
import { groupErrorMessage } from '@/lib/groupErrors'

/**
 * Removing a member, or leaving (`self`): `DELETE /groups/{id}/members/{userId}` with the member's
 * `ETag`; the checkbox (default on) decides whether their personal event shares go too.
 */
const props = defineProps<{
  /** The member to remove; for leaving, one's own membership (its `etag` is the `If-Match`). */
  member: MemberResponse | null
  self: boolean
}>()
const open = defineModel<boolean>('open', { default: false })

const { t } = useI18n()
const toast = useToast()
const router = useRouter()
const queryClient = useQueryClient()
const { id, group } = useCurrentGroup()
const run = useGroupChange(id)

const revokeShares = ref(true)
const pending = ref(false)
const error = ref<string | null>(null)
const name = computed(() => props.member?.displayName ?? '')
const groupName = computed(() => group.value?.name ?? '')

watch(open, (isOpen) => {
  if (!isOpen) return
  revokeShares.value = true
  error.value = null
})

async function submit() {
  const member = props.member
  if (!member) return
  pending.value = true
  error.value = null
  const groupId = id.value
  // The member is gone from the reloaded list afterwards: keep the name for the toast.
  const removed = name.value
  try {
    const remove = () =>
      call(
        api.DELETE('/api/v1/groups/{id}/members/{userId}', {
          params: {
            path: { id: groupId, userId: member.userId },
            query: revokeShares.value ? {} : { revokeEventShares: false },
            header: { 'If-Match': member.etag },
          },
        }),
      )
    if (props.self) {
      const left = groupName.value
      await remove()
      open.value = false
      await router.push({ name: 'groups' })
      await forgetGroup(queryClient, groupId)
      toast.success(t('groups.leaveDialog.left', { group: left }))
    } else {
      await run(remove)
      open.value = false
      toast.success(t('groups.removeDialog.removed', { name: removed }))
    }
  } catch (e) {
    if (props.self) await queryClient.invalidateQueries({ queryKey: ['groups', groupId] })
    error.value = groupErrorMessage(
      t,
      e,
      props.self ? { kind: 'leave' } : { kind: 'remove', name: name.value },
    )
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="
      self
        ? t('groups.leaveDialog.title', { group: groupName })
        : t('groups.removeDialog.title', { name })
    "
    :description="
      self ? t('groups.leaveDialog.description') : t('groups.removeDialog.description', { name })
    "
    :close-label="t('common.close')"
    :testid="self ? 'leave-dialog' : 'remove-dialog'"
  >
    <form id="remove-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <FormAlert :message="error" />
      <UiCheckbox
        v-model="revokeShares"
        :label="t('groups.revokeShares.label')"
        :description="
          self ? t('groups.revokeShares.selfHint') : t('groups.revokeShares.hint', { name })
        "
        data-testid="revoke-shares"
      />
    </form>
    <template #footer>
      <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
      <UiButton
        type="submit"
        form="remove-form"
        variant="danger"
        :loading="pending"
        data-testid="remove-submit"
      >
        {{ self ? t('groups.leaveDialog.submit') : t('groups.removeDialog.submit') }}
      </UiButton>
    </template>
  </UiDialog>
</template>
