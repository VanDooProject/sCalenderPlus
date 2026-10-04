<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { Unlink, UsersRound } from '@lucide/vue'
import { UiButton, useToast } from '@scalenderplus/ui'
import { api } from '@/api'
import AuthHeading from '@/components/AuthHeading.vue'
import FormAlert from '@/components/FormAlert.vue'
import { useSession } from '@/composables/session'
import { call } from '@/lib/apiError'
import { errorMessage } from '@/lib/errorMessages'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const toast = useToast()
const queryClient = useQueryClient()
const { user, query } = useSession()

const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : ''))
const here = computed(() => route.fullPath)
const pending = ref(false)
const error = ref<string | null>(null)

async function accept() {
  pending.value = true
  error.value = null
  try {
    const { data } = await call(
      api.POST('/api/v1/invites/accept', { body: { token: token.value } }),
    )
    await queryClient.invalidateQueries({ queryKey: ['groups'] })
    await queryClient.invalidateQueries({ queryKey: ['calendars'] })
    toast.success(t('invite.joined', { name: data.name }))
    await router.replace({ name: 'groups' })
  } catch (e) {
    error.value = errorMessage(t, e)
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <div v-if="!token" data-testid="invite-invalid">
    <AuthHeading :title="t('auth.linkInvalidTitle')" :description="t('invite.invalidText')">
      <template #icon><Unlink aria-hidden="true" /></template>
    </AuthHeading>
  </div>

  <div v-else>
    <AuthHeading :title="t('invite.title')" :description="t('invite.description')">
      <template #icon><UsersRound aria-hidden="true" /></template>
    </AuthHeading>
    <div v-if="query.isPending.value" />
    <div v-else-if="user" class="flex flex-col gap-4">
      <FormAlert :message="error" />
      <p class="text-sm text-on-surface-muted">
        {{ t('invite.signedInAs', { email: user.email }) }}
      </p>
      <UiButton size="lg" block :loading="pending" data-testid="invite-accept" @click="accept">
        {{ t('invite.accept') }}
      </UiButton>
    </div>
    <div v-else class="flex flex-col gap-3">
      <p class="text-sm text-on-surface-muted">{{ t('invite.signInFirst') }}</p>
      <UiButton as-child size="lg" block>
        <RouterLink :to="{ name: 'login', query: { next: here } }">{{
          t('auth.login.submit')
        }}</RouterLink>
      </UiButton>
      <UiButton as-child size="lg" block variant="secondary">
        <RouterLink :to="{ name: 'register', query: { next: here } }">
          {{ t('auth.register.submit') }}
        </RouterLink>
      </UiButton>
    </div>
  </div>
</template>
