<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { UiButton, UiDialog, useToast } from '@scalenderplus/ui'
import type { MemberResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import { useCurrentGroup } from '@/composables/currentGroup'
import { useGroupChange } from '@/composables/groups'
import { call } from '@/lib/apiError'
import { groupErrorMessage } from '@/lib/groupErrors'

const props = defineProps<{ member: MemberResponse | null }>()
const open = defineModel<boolean>('open', { default: false })

const { t } = useI18n()
const toast = useToast()
const { id } = useCurrentGroup()
const run = useGroupChange(id)
const pending = ref(false)
const error = ref<string | null>(null)
const name = computed(() => props.member?.displayName ?? '')

watch(open, (isOpen) => {
  if (isOpen) error.value = null
})

async function submit() {
  const member = props.member
  if (!member) return
  pending.value = true
  error.value = null
  const recipient = name.value
  try {
    await run(() =>
      call(
        api.POST('/api/v1/groups/{id}/transfer', {
          params: { path: { id: id.value } },
          body: { userId: member.userId },
        }),
      ),
    )
    open.value = false
    toast.success(t('groups.transferDialog.transferred', { name: recipient }))
  } catch (e) {
    error.value = groupErrorMessage(t, e, { kind: 'transfer', name: name.value })
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="t('groups.transferDialog.title', { name })"
    :description="t('groups.transferDialog.description')"
    :close-label="t('common.close')"
    testid="transfer-dialog"
  >
    <FormAlert :message="error" />
    <template #footer>
      <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
      <UiButton :loading="pending" data-testid="transfer-submit" @click="submit">
        {{ t('groups.transferDialog.submit') }}
      </UiButton>
    </template>
  </UiDialog>
</template>
