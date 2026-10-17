<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { UiButton, UiDialog, UiField, UiInput, useToast } from '@scalenderplus/ui'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import { useForm, useValidators } from '@/composables/form'
import { offerUpgrade } from '@/composables/paywall'
import { groupKeys } from '@/composables/groups'
import { call } from '@/lib/apiError'

const open = defineModel<boolean>('open', { default: false })
/** The page of the new group to go to (the onboarding checklist goes on to its invites). */
const props = withDefaults(defineProps<{ next?: 'group-members' | 'group-invites' }>(), {
  next: 'group-members',
})

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const queryClient = useQueryClient()
const rules = useValidators()
const formRef = ref<HTMLFormElement>()

const form = useForm(
  { name: '', description: '' },
  {
    formRef,
    validate: (v) => ({ name: rules.required(v.name) }),
    // owned_groups counts my own groups: my plan.
    onError: (error) => offerUpgrade(error, { owner: 'me' }),
  },
)

watch(open, (isOpen) => {
  if (isOpen) form.reset()
})

async function submit() {
  await form.submit(async (v) => {
    const description = v.description.trim()
    const { data } = await call(
      api.POST('/api/v1/groups', {
        body: { name: v.name.trim(), ...(description ? { description } : {}) },
      }),
    )
    await queryClient.invalidateQueries({ queryKey: groupKeys.list() })
    open.value = false
    toast.success(t('groups.createDialog.created', { name: data.name }))
    await router.push({ name: props.next, params: { groupId: data.id } })
  })
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="t('groups.createDialog.title')"
    :description="t('groups.createDialog.description')"
    :close-label="t('common.close')"
    testid="create-group-dialog"
  >
    <form
      id="create-group-form"
      ref="formRef"
      class="flex flex-col gap-5"
      novalidate
      data-testid="create-group-form"
      @submit.prevent="submit"
    >
      <FormAlert :message="form.formError.value" />
      <UiField :label="t('groups.createDialog.name')" :errors="form.errors('name')" required>
        <UiInput
          v-model="form.values.name"
          name="name"
          maxlength="100"
          autocomplete="off"
          data-testid="group-name"
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
          data-testid="group-description"
        />
      </UiField>
    </form>
    <template #footer>
      <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
      <UiButton
        type="submit"
        form="create-group-form"
        :loading="form.pending.value"
        data-testid="create-group-submit"
      >
        {{ t('groups.createDialog.submit') }}
      </UiButton>
    </template>
  </UiDialog>
</template>
