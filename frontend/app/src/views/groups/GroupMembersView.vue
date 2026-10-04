<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { CreditCard, EllipsisVertical, EyeOff, LogOut, ShieldCheck, UserMinus } from '@lucide/vue'
import { UiButton, UiMenu, UiMenuItem, UiMenuSeparator } from '@scalenderplus/ui'
import type { GroupRole, MemberResponse } from '@scalenderplus/api-client'
import UserAvatar from '@/components/UserAvatar.vue'
import MemberRoleDialog from '@/components/groups/MemberRoleDialog.vue'
import RemoveMemberDialog from '@/components/groups/RemoveMemberDialog.vue'
import RoleBadge from '@/components/groups/RoleBadge.vue'
import TransferBillingDialog from '@/components/groups/TransferBillingDialog.vue'
import { useCurrentGroup } from '@/composables/currentGroup'
import { useMembers } from '@/composables/groups'
import { useSession } from '@/composables/session'
import { isApiError } from '@/lib/errorMessages'
import {
  asRole,
  assignableRoles,
  canRemove,
  canTransferBillingTo,
  groupRoles,
  ownerLeavingBlock,
  type MemberContext,
} from '@/lib/groupRoles'

const { t } = useI18n()
const { user } = useSession()
const { id, myRole, isBillingOwner } = useCurrentGroup()
const members = useMembers(id)

const hidden = computed(() => isApiError(members.error.value, 'insufficient_permission'))
const ownerCount = computed(
  () => (members.data.value ?? []).filter((m) => m.role === 'owner').length,
)

function context(member: MemberResponse): MemberContext {
  return {
    role: asRole(member.role),
    isSelf: member.userId === user.value?.id,
    isBillingOwner: member.isBillingOwner,
    ownerCount: ownerCount.value,
  }
}

interface Row {
  member: MemberResponse
  self: boolean
  roles: GroupRole[]
  canChangeRole: boolean
  canRemove: boolean
  canTransfer: boolean
  /** Why this owner can't leave or be demoted now (shown as a hint; the api decides). */
  block: ReturnType<typeof ownerLeavingBlock>
}

/** Owners first, then by name; one's own row first among equals. */
const rows = computed<Row[]>(() =>
  [...(members.data.value ?? [])]
    .sort(
      (a, b) =>
        groupRoles.indexOf(asRole(b.role)) - groupRoles.indexOf(asRole(a.role)) ||
        Number(b.userId === user.value?.id) - Number(a.userId === user.value?.id) ||
        a.displayName.localeCompare(b.displayName),
    )
    .map((member) => {
      const target = context(member)
      const roles = assignableRoles(myRole.value, target)
      return {
        member,
        self: target.isSelf,
        roles,
        canChangeRole: roles.some((role) => role !== target.role),
        canRemove: canRemove(myRole.value, target),
        canTransfer: canTransferBillingTo(myRole.value, isBillingOwner.value, target),
        block: ownerLeavingBlock(target),
      }
    }),
)

const myMembership = computed(() => rows.value.find((row) => row.self)?.member ?? null)

// One dialog of each kind; the member is looked up from the list, so a reload (412) refreshes
// its `etag`.
const dialog = ref<'role' | 'remove' | 'leave' | 'transfer' | null>(null)
const selectedId = ref<string | null>(null)
const selected = computed(() => rows.value.find((row) => row.member.userId === selectedId.value))

function openDialog(kind: NonNullable<typeof dialog.value>, member: MemberResponse) {
  selectedId.value = member.userId
  dialog.value = kind
}

function dialogModel(kind: NonNullable<typeof dialog.value>) {
  return computed({
    get: () => dialog.value === kind,
    set: (open: boolean) => {
      if (!open && dialog.value === kind) dialog.value = null
    },
  })
}
const roleOpen = dialogModel('role')
const removeOpen = dialogModel('remove')
const leaveOpen = dialogModel('leave')
const transferOpen = dialogModel('transfer')

const blockHint = (block: Row['block']) =>
  block === 'last_owner'
    ? t('groups.members.lastOwnerHint')
    : block === 'billing_owner_transfer_required'
      ? t('groups.members.billingOwnerHint')
      : null
</script>

<template>
  <section class="flex flex-col gap-4" aria-labelledby="members-title">
    <div class="flex items-center justify-between gap-3">
      <h2 id="members-title" class="text-base font-semibold">{{ t('groups.members.title') }}</h2>
      <UiButton
        v-if="myMembership"
        variant="secondary"
        size="sm"
        data-testid="leave-group"
        @click="openDialog('leave', myMembership)"
      >
        <LogOut class="size-4" aria-hidden="true" />{{ t('groups.members.leave') }}
      </UiButton>
    </div>

    <ul v-if="members.isPending.value" class="flex flex-col gap-2" aria-hidden="true">
      <li v-for="i in 3" :key="i" class="h-16 animate-pulse rounded-xl bg-surface-hover" />
    </ul>

    <div
      v-else-if="hidden"
      class="flex items-start gap-3 rounded-2xl border border-border bg-surface-raised p-5 text-sm text-on-surface-muted"
      data-testid="members-hidden"
    >
      <EyeOff class="size-5 shrink-0" aria-hidden="true" />
      {{ t('groups.members.hidden') }}
    </div>

    <div
      v-else-if="members.isError.value"
      class="flex flex-col items-start gap-3 rounded-2xl border border-border bg-surface-raised p-5 text-sm"
      role="alert"
    >
      <p>{{ t('groups.members.loadError') }}</p>
      <UiButton variant="secondary" size="sm" @click="members.refetch()">
        {{ t('common.retry') }}
      </UiButton>
    </div>

    <ul
      v-else
      class="divide-y divide-border overflow-hidden rounded-2xl border border-border bg-surface-raised shadow-xs"
      data-testid="member-list"
    >
      <li
        v-for="row in rows"
        :key="row.member.userId"
        class="flex items-center gap-3 px-4 py-3 sm:px-5"
        :data-testid="`member-${row.member.userId}`"
      >
        <UserAvatar :name="row.member.displayName" />
        <div class="flex min-w-0 flex-1 flex-col gap-0.5">
          <p class="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            <span class="truncate font-medium" data-testid="member-name">{{
              row.member.displayName
            }}</span>
            <span
              v-if="row.self"
              class="rounded-full bg-surface-hover px-2 py-0.5 text-xs font-medium text-on-surface-muted"
            >
              {{ t('groups.you') }}
            </span>
            <span
              v-if="row.member.isBillingOwner"
              class="inline-flex items-center gap-1 rounded-full bg-success-soft px-2 py-0.5 text-xs font-medium text-on-success-soft"
              data-testid="billing-owner-badge"
            >
              <CreditCard class="size-3.5" aria-hidden="true" />{{ t('groups.billingOwner') }}
            </span>
          </p>
          <p v-if="row.member.email" class="truncate text-sm text-on-surface-muted">
            {{ row.member.email }}
          </p>
          <p
            v-if="row.block && (row.self || row.canChangeRole || row.canRemove)"
            class="text-xs text-on-surface-muted"
            :data-testid="`member-hint-${row.block}`"
          >
            {{ blockHint(row.block) }}
          </p>
        </div>
        <RoleBadge :role="row.member.role" />
        <UiMenu
          v-if="row.canChangeRole || row.canRemove || row.canTransfer"
          :testid="`member-menu-${row.member.userId}`"
        >
          <template #trigger>
            <UiButton
              variant="ghost"
              size="icon"
              :aria-label="t('groups.members.actions', { name: row.member.displayName })"
              :data-testid="`member-actions-${row.member.userId}`"
            >
              <EllipsisVertical class="size-5" aria-hidden="true" />
            </UiButton>
          </template>
          <UiMenuItem
            v-if="row.canChangeRole"
            data-testid="action-change-role"
            @select="openDialog('role', row.member)"
          >
            <ShieldCheck aria-hidden="true" />{{ t('groups.members.changeRole') }}
          </UiMenuItem>
          <UiMenuItem
            v-if="row.canTransfer"
            data-testid="action-transfer"
            @select="openDialog('transfer', row.member)"
          >
            <CreditCard aria-hidden="true" />{{ t('groups.members.makeBillingOwner') }}
          </UiMenuItem>
          <template v-if="row.canRemove">
            <UiMenuSeparator v-if="row.canChangeRole || row.canTransfer" />
            <UiMenuItem
              danger
              :data-testid="row.self ? 'action-leave' : 'action-remove'"
              @select="openDialog(row.self ? 'leave' : 'remove', row.member)"
            >
              <LogOut v-if="row.self" aria-hidden="true" />
              <UserMinus v-else aria-hidden="true" />
              {{ row.self ? t('groups.members.leave') : t('groups.members.remove') }}
            </UiMenuItem>
          </template>
        </UiMenu>
        <span v-else class="size-10 shrink-0" aria-hidden="true" />
      </li>
    </ul>

    <MemberRoleDialog
      v-model:open="roleOpen"
      :member="selected?.member ?? null"
      :self="selected?.self ?? false"
      :roles="selected?.roles ?? []"
    />
    <RemoveMemberDialog
      v-model:open="removeOpen"
      :member="selected?.member ?? null"
      :self="false"
    />
    <RemoveMemberDialog
      v-model:open="leaveOpen"
      :member="selected?.member ?? myMembership"
      :self="true"
    />
    <TransferBillingDialog v-model:open="transferOpen" :member="selected?.member ?? null" />
  </section>
</template>
