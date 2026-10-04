import { inject, provide, type ComputedRef, type InjectionKey } from 'vue'
import type { GroupResponse, GroupRole } from '@scalenderplus/api-client'
import { asRole } from '@/lib/groupRoles'

/** The group of the `/groups/:groupId` pages, provided by their layout (`GroupView`). */
export interface CurrentGroup {
  id: ComputedRef<string>
  group: ComputedRef<GroupResponse | null>
  /** `ETag` of `GET /groups/{id}`: the `If-Match` of group PATCH/DELETE. */
  etag: ComputedRef<string | null>
  /** The signed-in user's role (lowest while loading). */
  myRole: ComputedRef<GroupRole>
  /** The signed-in user is the billing owner. */
  isBillingOwner: ComputedRef<boolean>
}

const key: InjectionKey<CurrentGroup> = Symbol('current-group')

export function provideCurrentGroup(value: CurrentGroup) {
  provide(key, value)
}

export function useCurrentGroup(): CurrentGroup {
  const value = inject(key)
  if (!value) throw new Error('useCurrentGroup() outside a group page')
  return value
}

export function roleOf(group: GroupResponse | null): GroupRole {
  return group ? asRole(group.myRole) : 'viewer'
}
