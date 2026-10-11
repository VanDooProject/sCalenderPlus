import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { useQueries } from '@tanstack/vue-query'
import { membersQuery, useGroups } from '@/composables/groups'
import { useSession } from '@/composables/session'

/** A person or group that can be named by an override or explained. */
export interface PrincipalCandidate {
  type: 'user' | 'group'
  id: string
  name: string
}

/**
 * People sharing a group with me and my groups (the principals the api lets me select,
 * permissions.md §4.4), loaded while `enabled`; sorted by name, without me.
 */
export function usePrincipalCandidates(enabled: MaybeRefOrGetter<boolean>) {
  const { user } = useSession()
  const groups = useGroups()
  const memberLists = useQueries({
    queries: computed(() =>
      toValue(enabled)
        ? (groups.data.value ?? []).map((g) => ({ ...membersQuery(g.id), retry: false }))
        : [],
    ),
  })
  const people = computed<PrincipalCandidate[]>(() => {
    const byId = new Map<string, string>()
    for (const list of memberLists.value) {
      for (const member of list.data ?? []) {
        if (member.userId !== user.value?.id) byId.set(member.userId, member.displayName)
      }
    }
    return [...byId.entries()]
      .map(([id, name]) => ({ type: 'user' as const, id, name }))
      .sort((a, b) => a.name.localeCompare(b.name))
  })
  const myGroups = computed<PrincipalCandidate[]>(() =>
    (groups.data.value ?? [])
      .map((g) => ({ type: 'group' as const, id: g.id, name: g.name }))
      .sort((a, b) => a.name.localeCompare(b.name)),
  )
  return { people, groups: myGroups }
}
