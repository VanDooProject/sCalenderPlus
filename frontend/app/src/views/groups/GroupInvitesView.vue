<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQueryClient } from '@tanstack/vue-query'
import { Copy, Link2, Mail, MailWarning, Trash2 } from '@lucide/vue'
import {
  UiAlert,
  UiButton,
  UiDialog,
  UiField,
  UiInput,
  UiSelect,
  useToast,
} from '@scalenderplus/ui'
import type { GroupRole, InviteResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import FormAlert from '@/components/FormAlert.vue'
import RoleBadge from '@/components/groups/RoleBadge.vue'
import SettingsCard from '@/components/SettingsCard.vue'
import { useCurrentGroup } from '@/composables/currentGroup'
import { groupKeys, useInvites } from '@/composables/groups'
import { useForm, useValidators } from '@/composables/form'
import { useSession } from '@/composables/session'
import { call } from '@/lib/apiError'
import { groupErrorMessage } from '@/lib/groupErrors'
import { asRole, canManageInvites, canRevokeInvite, inviteRoles } from '@/lib/groupRoles'

const { t, locale } = useI18n()
const toast = useToast()
const queryClient = useQueryClient()
const rules = useValidators()
const { user } = useSession()
const { id, group, myRole } = useCurrentGroup()

const allowed = computed(() => canManageInvites(myRole.value))
const invites = useInvites(id, allowed)
const verified = computed(() => user.value?.emailVerified ?? false)

const roleOptions = (kind: 'email' | 'link') =>
  inviteRoles(myRole.value, kind).map((value) => ({ value, label: t(`groups.roles.${value}`) }))
const emailRoles = computed(() => roleOptions('email'))
const linkRoles = computed(() => roleOptions('link'))
const expiryOptions = computed(() =>
  ['1', '3', '7', '14', '30'].map((value) => ({
    value,
    label: t('groups.invites.days', Number(value)),
  })),
)

const refreshInvites = () =>
  queryClient.invalidateQueries({ queryKey: groupKeys.invites(id.value) })

// Invite by email.
const emailFormRef = ref<HTMLFormElement>()
const emailForm = useForm(
  { email: '', role: 'member' as GroupRole },
  {
    formRef: emailFormRef,
    validate: (v) => ({ email: rules.email(v.email) }),
    errorMessage: (error) => groupErrorMessage(t, error, { kind: 'other' }),
  },
)

async function sendEmailInvite() {
  await emailForm.submit(async (v) => {
    await call(
      api.POST('/api/v1/groups/{id}/invites', {
        params: { path: { id: id.value } },
        body: { email: v.email.trim(), role: v.role },
      }),
    )
    toast.success(t('groups.invites.sent', { email: v.email.trim() }))
    emailForm.reset({ email: '', role: v.role })
    await refreshInvites()
  })
}

// Invite link.
const linkFormRef = ref<HTMLFormElement>()
const linkForm = useForm(
  { role: 'member' as GroupRole, expiresInDays: '7', maxUses: '50' },
  {
    formRef: linkFormRef,
    errorMessage: (error) => groupErrorMessage(t, error, { kind: 'other' }),
    validate: (v) => {
      const uses = Number(v.maxUses)
      return {
        maxUses:
          Number.isInteger(uses) && uses >= 1 && uses <= 1000
            ? undefined
            : t('groups.invites.maxUsesHint'),
      }
    },
  },
)
/** The new link with its token: shown once, never listed again. */
const createdLink = ref<string | null>(null)

async function createLink() {
  await linkForm.submit(async (v) => {
    const { data } = await call(
      api.POST('/api/v1/groups/{id}/invites', {
        params: { path: { id: id.value } },
        body: { role: v.role, expiresInDays: Number(v.expiresInDays), maxUses: Number(v.maxUses) },
      }),
    )
    createdLink.value = data.url ?? null
    await refreshInvites()
  })
}

async function copyLink() {
  if (!createdLink.value) return
  try {
    await navigator.clipboard.writeText(createdLink.value)
    toast.success(t('groups.invites.linkCopied'))
  } catch {
    toast.error(t('groups.invites.copyFailed'))
  }
}

// Revoking.
const revoking = ref<InviteResponse | null>(null)
const revokeOpen = computed({
  get: () => revoking.value !== null,
  set: (open: boolean) => {
    if (!open) revoking.value = null
  },
})
const revokePending = ref(false)
const revokeError = ref<string | null>(null)
watch(revoking, () => (revokeError.value = null))

async function revoke() {
  const invite = revoking.value
  if (!invite) return
  revokePending.value = true
  try {
    await call(api.DELETE('/api/v1/invites/{id}', { params: { path: { id: invite.id } } }))
    revoking.value = null
    toast.success(t('groups.invites.revoked'))
    await refreshInvites()
  } catch (error) {
    revokeError.value = groupErrorMessage(t, error, { kind: 'other' })
  } finally {
    revokePending.value = false
  }
}

const target = (invite: InviteResponse) => invite.email ?? t('groups.invites.linkInvite')
const dateFormat = computed(
  () => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }),
)
const expires = (invite: InviteResponse) =>
  t('groups.invites.expires', { date: dateFormat.value.format(new Date(invite.expiresAt)) })
</script>

<template>
  <div class="flex flex-col gap-6">
    <UiAlert v-if="!allowed" data-testid="invites-forbidden">
      {{ t('groups.invites.noPermission') }}
    </UiAlert>

    <template v-else>
      <UiAlert v-if="!verified" variant="warning" data-testid="invites-verify-first">
        <template #icon><MailWarning aria-hidden="true" /></template>
        {{ t('groups.invites.verifyFirst') }}
      </UiAlert>

      <div class="grid gap-6 lg:grid-cols-2">
        <form
          ref="emailFormRef"
          novalidate
          data-testid="invite-email-form"
          @submit.prevent="sendEmailInvite"
        >
          <SettingsCard
            :title="t('groups.invites.byEmail')"
            :description="t('groups.invites.byEmailText')"
          >
            <div class="flex flex-col gap-5">
              <FormAlert :message="emailForm.formError.value" />
              <UiField
                :label="t('groups.invites.email')"
                :errors="emailForm.errors('email')"
                required
              >
                <UiInput
                  v-model="emailForm.values.email"
                  type="email"
                  name="email"
                  autocomplete="off"
                  inputmode="email"
                  data-testid="invite-email"
                />
              </UiField>
              <UiField
                :label="t('groups.invites.role')"
                :hint="t(`groups.roleHints.${emailForm.values.role}`)"
                :errors="emailForm.errors('role')"
              >
                <UiSelect
                  v-model="emailForm.values.role"
                  :options="emailRoles"
                  testid="invite-email-role"
                />
              </UiField>
            </div>
            <template #footer>
              <UiButton
                type="submit"
                :disabled="!verified || group?.frozen"
                :loading="emailForm.pending.value"
                data-testid="invite-email-submit"
              >
                <Mail class="size-4" aria-hidden="true" />{{ t('groups.invites.send') }}
              </UiButton>
            </template>
          </SettingsCard>
        </form>

        <form
          ref="linkFormRef"
          novalidate
          data-testid="invite-link-form"
          @submit.prevent="createLink"
        >
          <SettingsCard
            :title="t('groups.invites.byLink')"
            :description="t('groups.invites.byLinkText')"
          >
            <div class="flex flex-col gap-5">
              <FormAlert :message="linkForm.formError.value" />
              <UiField
                :label="t('groups.invites.role')"
                :hint="t(`groups.roleHints.${linkForm.values.role}`)"
                :errors="linkForm.errors('role')"
              >
                <UiSelect
                  v-model="linkForm.values.role"
                  :options="linkRoles"
                  testid="invite-link-role"
                />
              </UiField>
              <div class="grid gap-5 sm:grid-cols-2">
                <UiField
                  :label="t('groups.invites.expiresIn')"
                  :errors="linkForm.errors('expiresInDays')"
                >
                  <UiSelect
                    v-model="linkForm.values.expiresInDays"
                    :options="expiryOptions"
                    testid="invite-link-expiry"
                  />
                </UiField>
                <UiField
                  :label="t('groups.invites.maxUses')"
                  :hint="t('groups.invites.maxUsesHint')"
                  :errors="linkForm.errors('maxUses')"
                >
                  <UiInput
                    v-model="linkForm.values.maxUses"
                    inputmode="numeric"
                    pattern="[0-9]*"
                    name="maxUses"
                    data-testid="invite-link-max-uses"
                  />
                </UiField>
              </div>
              <div
                v-if="createdLink"
                class="flex flex-col gap-2 rounded-xl border border-border bg-surface-muted p-4"
                data-testid="invite-link-created"
                aria-live="polite"
              >
                <p class="text-sm font-medium">{{ t('groups.invites.linkCreated') }}</p>
                <p class="text-sm text-on-surface-muted">{{ t('groups.invites.linkOnce') }}</p>
                <div class="flex gap-2">
                  <UiField :label="t('groups.invites.linkLabel')" hide-label class="min-w-0 flex-1">
                    <UiInput
                      :model-value="createdLink"
                      readonly
                      data-testid="invite-link-url"
                      @focus="($event.target as HTMLInputElement).select()"
                    />
                  </UiField>
                  <UiButton variant="secondary" data-testid="invite-link-copy" @click="copyLink">
                    <Copy class="size-4" aria-hidden="true" />{{ t('groups.invites.copyLink') }}
                  </UiButton>
                </div>
              </div>
            </div>
            <template #footer>
              <UiButton
                type="submit"
                :disabled="!verified || group?.frozen"
                :loading="linkForm.pending.value"
                data-testid="invite-link-submit"
              >
                <Link2 class="size-4" aria-hidden="true" />{{ t('groups.invites.createLink') }}
              </UiButton>
            </template>
          </SettingsCard>
        </form>
      </div>

      <section class="flex flex-col gap-3" aria-labelledby="pending-title">
        <h2 id="pending-title" class="text-base font-semibold">
          {{ t('groups.invites.pending') }}
        </h2>
        <ul v-if="invites.isPending.value" class="flex flex-col gap-2" aria-hidden="true">
          <li v-for="i in 2" :key="i" class="h-14 animate-pulse rounded-xl bg-surface-hover" />
        </ul>
        <div
          v-else-if="invites.isError.value"
          class="flex flex-col items-start gap-3 rounded-2xl border border-border bg-surface-raised p-5 text-sm"
          role="alert"
        >
          <p>{{ t('groups.invites.loadError') }}</p>
          <UiButton variant="secondary" size="sm" @click="invites.refetch()">
            {{ t('common.retry') }}
          </UiButton>
        </div>
        <p
          v-else-if="invites.data.value?.length === 0"
          class="rounded-2xl border border-dashed border-border-strong px-5 py-6 text-center text-sm text-on-surface-muted"
          data-testid="invites-empty"
        >
          {{ t('groups.invites.pendingEmpty') }}
        </p>
        <ul
          v-else
          class="divide-y divide-border overflow-hidden rounded-2xl border border-border bg-surface-raised shadow-xs"
          data-testid="invite-list"
        >
          <li
            v-for="invite in invites.data.value"
            :key="invite.id"
            class="flex items-center gap-3 px-4 py-3 sm:px-5"
            :data-testid="`invite-${invite.id}`"
          >
            <span
              class="flex size-8 shrink-0 items-center justify-center rounded-full bg-surface-hover text-on-surface-muted"
              aria-hidden="true"
            >
              <Mail v-if="invite.kind === 'email'" class="size-4" />
              <Link2 v-else class="size-4" />
            </span>
            <div class="flex min-w-0 flex-1 flex-col gap-0.5 text-sm">
              <span class="truncate font-medium">{{ target(invite) }}</span>
              <span class="text-on-surface-muted">
                <template v-if="invite.kind === 'link'">
                  {{ t('groups.invites.uses', { uses: invite.uses, max: invite.maxUses }) }} ·
                </template>
                {{ expires(invite) }}
              </span>
            </div>
            <RoleBadge :role="invite.role" />
            <UiButton
              v-if="canRevokeInvite(myRole, asRole(invite.role))"
              variant="ghost"
              size="icon"
              :aria-label="t('groups.invites.revokeLabel', { target: target(invite) })"
              data-testid="invite-revoke"
              @click="revoking = invite"
            >
              <Trash2 class="size-4" aria-hidden="true" />
            </UiButton>
            <span v-else class="size-10 shrink-0" aria-hidden="true" />
          </li>
        </ul>
      </section>
    </template>

    <UiDialog
      v-model:open="revokeOpen"
      :title="t('groups.invites.revokeTitle')"
      :description="t('groups.invites.revokeText')"
      :close-label="t('common.close')"
      size="sm"
      testid="revoke-dialog"
    >
      <FormAlert :message="revokeError" />
      <p v-if="revoking" class="text-sm font-medium break-all">{{ target(revoking) }}</p>
      <template #footer>
        <UiButton variant="ghost" @click="revoking = null">{{ t('common.cancel') }}</UiButton>
        <UiButton
          variant="danger"
          :loading="revokePending"
          data-testid="revoke-submit"
          @click="revoke"
        >
          {{ t('groups.invites.revoke') }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>
