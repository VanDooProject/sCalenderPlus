<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { MailWarning, Snowflake, Unlink, UserRoundX, UsersRound } from '@lucide/vue'
import { UiButton, UiSpinner, useToast } from '@scalenderplus/ui'
import { api } from '@/api'
import AuthHeading from '@/components/AuthHeading.vue'
import FormAlert from '@/components/FormAlert.vue'
import { groupKeys, previewInvite } from '@/composables/groups'
import { useLogout, useSession } from '@/composables/session'
import { ApiError, call } from '@/lib/apiError'
import { errorMessage, isApiError } from '@/lib/errorMessages'
import { asRole } from '@/lib/groupRoles'

/**
 * `/invite?token=…`: shows what the invite leads to (`POST /invites/preview`, also signed out),
 * sends signed-out visitors through login or sign-up (`next` = this page), then accepts.
 */
const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const toast = useToast()
const queryClient = useQueryClient()
const { user, query: session } = useSession()
const logout = useLogout()

const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : ''))
const here = computed(() => route.fullPath)

const preview = useQuery(
  computed(() => ({
    queryKey: ['invite-preview', token.value] as const,
    queryFn: () => previewInvite(token.value),
    enabled: token.value !== '',
    retry: (count: number, error: unknown) => !(error instanceof ApiError) && count < 2,
    staleTime: 60_000,
  })),
)

/** Outcome of accepting that replaces the page: the api's reason, explained. */
const blocked = ref<'mismatch' | 'unverified' | 'frozen' | 'invalid' | null>(null)
const error = ref<string | null>(null)

const state = computed(() => {
  if (!token.value) return 'incomplete'
  if (blocked.value) return blocked.value
  if (preview.isPending.value || session.isPending.value) return 'loading'
  if (isApiError(preview.error.value, 'token_invalid')) return 'invalid'
  if (preview.isError.value) return 'error'
  if (!user.value) return 'signed-out'
  if (!user.value.emailVerified) return 'unverified'
  return 'ready'
})

const roleName = computed(() =>
  preview.data.value ? t(`groups.roles.${asRole(preview.data.value.role)}`) : '',
)
const expiry = computed(() =>
  preview.data.value
    ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'long', timeStyle: 'short' }).format(
        new Date(preview.data.value.expiresAt),
      )
    : '',
)

const accept = useMutation({
  mutationFn: async () =>
    (await call(api.POST('/api/v1/invites/accept', { body: { token: token.value } }))).data,
  onSuccess: async (group) => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: groupKeys.all }),
      queryClient.invalidateQueries({ queryKey: ['calendars'] }),
    ])
    toast.success(t('invite.joined', { name: group.name }))
    await router.replace({ name: 'group-members', params: { groupId: group.id } })
  },
  onError: (e) => {
    if (isApiError(e, 'invite_email_mismatch')) blocked.value = 'mismatch'
    else if (isApiError(e, 'email_not_verified')) blocked.value = 'unverified'
    else if (isApiError(e, 'group_frozen')) blocked.value = 'frozen'
    else if (isApiError(e, 'token_invalid')) blocked.value = 'invalid'
    else error.value = errorMessage(t, e)
  },
})

function submit() {
  error.value = null
  accept.mutate()
}

const resend = useMutation({
  mutationFn: () => call(api.POST('/api/v1/auth/confirm-email/resend')),
  onSuccess: () => toast.success(t('verifyBanner.sent')),
})

/** Wrong account for an email invite: sign out and come back here through the login. */
async function switchAccount() {
  await logout.mutateAsync()
  await router.push({ name: 'login', query: { next: here.value } })
}
</script>

<template>
  <div v-if="state === 'loading'" class="flex flex-col items-center gap-4 py-8 text-center">
    <UiSpinner class="size-8 text-primary" :label="t('common.loading')" />
    <h1 class="text-lg font-semibold">{{ t('invite.title') }}</h1>
  </div>

  <div v-else-if="state === 'incomplete'" data-testid="invite-invalid">
    <AuthHeading :title="t('auth.linkInvalidTitle')" :description="t('invite.invalidText')">
      <template #icon><Unlink aria-hidden="true" /></template>
    </AuthHeading>
  </div>

  <div v-else-if="state === 'invalid'" data-testid="invite-expired">
    <AuthHeading :title="t('invite.invalidTitle')" :description="t('invite.expiredText')">
      <template #icon><Unlink aria-hidden="true" /></template>
    </AuthHeading>
    <UiButton as-child size="lg" block variant="secondary">
      <RouterLink :to="user ? { name: 'groups' } : { name: 'login' }">
        {{ user ? t('invite.toGroups') : t('auth.login.submit') }}
      </RouterLink>
    </UiButton>
  </div>

  <div v-else-if="state === 'error'" class="flex flex-col gap-5">
    <AuthHeading :title="t('invite.loadError')" />
    <FormAlert :message="errorMessage(t, preview.error.value)" />
    <UiButton size="lg" block @click="preview.refetch()">{{ t('common.retry') }}</UiButton>
  </div>

  <div v-else-if="state === 'mismatch'" data-testid="invite-mismatch">
    <AuthHeading
      :title="t('invite.mismatchTitle')"
      :description="t('invite.mismatchText', { email: user?.email ?? '' })"
    >
      <template #icon><UserRoundX aria-hidden="true" /></template>
    </AuthHeading>
    <UiButton size="lg" block :loading="logout.isPending.value" @click="switchAccount">
      {{ t('invite.switchAccount') }}
    </UiButton>
  </div>

  <div v-else-if="state === 'unverified'" data-testid="invite-unverified">
    <AuthHeading
      :title="t('invite.unverifiedTitle')"
      :description="t('invite.unverifiedText', { email: user?.email ?? '' })"
    >
      <template #icon><MailWarning aria-hidden="true" /></template>
    </AuthHeading>
    <UiButton size="lg" block :loading="resend.isPending.value" @click="resend.mutate()">
      {{ t('verifyBanner.resend') }}
    </UiButton>
  </div>

  <div v-else-if="state === 'frozen'" data-testid="invite-frozen">
    <AuthHeading :title="t('invite.frozenTitle')" :description="t('invite.frozenText')">
      <template #icon><Snowflake aria-hidden="true" /></template>
    </AuthHeading>
    <UiButton as-child size="lg" block variant="secondary">
      <RouterLink :to="{ name: 'groups' }">{{ t('invite.toGroups') }}</RouterLink>
    </UiButton>
  </div>

  <div v-else data-testid="invite-preview">
    <AuthHeading
      :title="t('invite.previewTitle', { group: preview.data.value?.groupName ?? '' })"
      :description="
        preview.data.value?.inviterName
          ? t('invite.invitedBy', { name: preview.data.value.inviterName, role: roleName })
          : t('invite.invitedAs', { role: roleName })
      "
    >
      <template #icon><UsersRound aria-hidden="true" /></template>
    </AuthHeading>
    <div class="flex flex-col gap-4">
      <p class="text-sm text-on-surface-muted" data-testid="invite-expiry">
        {{ t('invite.expires', { date: expiry }) }}
      </p>
      <template v-if="state === 'ready'">
        <FormAlert :message="error" />
        <p class="text-sm text-on-surface-muted">
          {{ t('invite.signedInAs', { email: user?.email ?? '' }) }}
        </p>
        <UiButton
          size="lg"
          block
          :loading="accept.isPending.value"
          data-testid="invite-accept"
          @click="submit"
        >
          {{ t('invite.accept') }}
        </UiButton>
      </template>
      <template v-else>
        <p class="text-sm text-on-surface-muted">{{ t('invite.signInFirst') }}</p>
        <UiButton as-child size="lg" block>
          <RouterLink :to="{ name: 'login', query: { next: here } }" data-testid="invite-login">
            {{ t('auth.login.submit') }}
          </RouterLink>
        </UiButton>
        <UiButton as-child size="lg" block variant="secondary">
          <RouterLink
            :to="{ name: 'register', query: { next: here } }"
            data-testid="invite-register"
          >
            {{ t('auth.register.submit') }}
          </RouterLink>
        </UiButton>
        <p class="text-xs text-on-surface-muted">{{ t('invite.afterSignUp') }}</p>
      </template>
    </div>
  </div>
</template>
