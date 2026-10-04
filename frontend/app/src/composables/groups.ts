import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { queryOptions, useQuery, useQueryClient, type QueryClient } from '@tanstack/vue-query'
import type {
  GroupResponse,
  InvitePreviewResponse,
  InviteResponse,
  MemberResponse,
} from '@scalenderplus/api-client'
import { api } from '@/api'
import { ApiError, call } from '@/lib/apiError'
import { isApiError } from '@/lib/errorMessages'

/**
 * Query keys of groups: everything below `['groups']`, so accepting an invite or leaving a group
 * can invalidate all of it at once.
 */
export const groupKeys = {
  all: ['groups'] as const,
  list: () => ['groups', 'list'] as const,
  detail: (id: string) => ['groups', id] as const,
  members: (id: string) => ['groups', id, 'members'] as const,
  invites: (id: string) => ['groups', id, 'invites'] as const,
}

/** A group with the strong `ETag` of `GET /groups/{id}` (the `If-Match` of PATCH/DELETE). */
export interface GroupWithEtag {
  group: GroupResponse
  etag: string | null
}

/** Reads every page of a cursor-paginated list (`{ items, nextCursor }`). */
async function allPages<T>(
  page: (cursor: string | undefined) => Promise<{ items: T[]; nextCursor?: string | null }>,
): Promise<T[]> {
  const items: T[] = []
  let cursor: string | undefined
  do {
    const result = await page(cursor)
    items.push(...result.items)
    cursor = result.nextCursor ?? undefined
  } while (cursor)
  return items
}

export function groupsQuery() {
  return queryOptions({
    queryKey: groupKeys.list(),
    queryFn: () =>
      allPages(
        async (cursor) =>
          (await call(api.GET('/api/v1/groups', { params: { query: { limit: 200, cursor } } })))
            .data,
      ),
  })
}

export function groupQuery(id: string) {
  return queryOptions({
    queryKey: groupKeys.detail(id),
    queryFn: async (): Promise<GroupWithEtag> => {
      const { data, response } = await call(
        api.GET('/api/v1/groups/{id}', { params: { path: { id } } }),
      )
      return { group: data, etag: response.headers.get('ETag') }
    },
    retry: (count, error) => !(error instanceof ApiError) && count < 2,
  })
}

export function membersQuery(id: string) {
  return queryOptions({
    queryKey: groupKeys.members(id),
    queryFn: (): Promise<MemberResponse[]> =>
      allPages(
        async (cursor) =>
          (
            await call(
              api.GET('/api/v1/groups/{id}/members', {
                params: { path: { id }, query: { limit: 200, cursor } },
              }),
            )
          ).data,
      ),
    retry: (count, error) => !(error instanceof ApiError) && count < 2,
    // Others join and leave: reload whenever the list is shown again.
    staleTime: 0,
  })
}

export function invitesQuery(id: string) {
  return queryOptions({
    queryKey: groupKeys.invites(id),
    queryFn: (): Promise<InviteResponse[]> =>
      allPages(
        async (cursor) =>
          (
            await call(
              api.GET('/api/v1/groups/{id}/invites', {
                params: { path: { id }, query: { limit: 200, cursor } },
              }),
            )
          ).data,
      ),
    retry: (count, error) => !(error instanceof ApiError) && count < 2,
    // Invites get used by others: reload whenever the list is shown again.
    staleTime: 0,
  })
}

export function useGroups() {
  return useQuery(groupsQuery())
}

export function useGroup(id: MaybeRefOrGetter<string>) {
  return useQuery(computed(() => groupQuery(toValue(id))))
}

export function useMembers(
  id: MaybeRefOrGetter<string>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  return useQuery(computed(() => ({ ...membersQuery(toValue(id)), enabled: toValue(enabled) })))
}

export function useInvites(
  id: MaybeRefOrGetter<string>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  return useQuery(computed(() => ({ ...invitesQuery(toValue(id)), enabled: toValue(enabled) })))
}

/** `POST /invites/preview`: what an invite token leads to (anonymous). */
export async function previewInvite(token: string): Promise<InvitePreviewResponse> {
  return (await call(api.POST('/api/v1/invites/preview', { body: { token } }))).data
}

/**
 * After a change of a group: reload its detail (new `ETag`), members and invites, and the list
 * (role, member count); calendars too, since group calendars follow memberships.
 */
export async function refreshGroup(queryClient: QueryClient, id: string) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: groupKeys.detail(id) }),
    queryClient.invalidateQueries({ queryKey: groupKeys.list() }),
  ])
}

/** After leaving or deleting a group: forget it, reload the list and the calendars. */
export async function forgetGroup(queryClient: QueryClient, id: string) {
  queryClient.removeQueries({ queryKey: groupKeys.detail(id) })
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: groupKeys.list() }),
    queryClient.invalidateQueries({ queryKey: ['calendars'] }),
  ])
}

/**
 * Runs a change guarded by `If-Match`; a `412` (changed elsewhere) reloads the group's data before
 * the error goes to the caller, so the next try uses the current version.
 */
export function useGroupChange(id: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return async function run<T>(change: () => Promise<T>): Promise<T> {
    try {
      const result = await change()
      await refreshGroup(queryClient, toValue(id))
      return result
    } catch (error) {
      if (isApiError(error, 'precondition_failed', 'precondition_required')) {
        await refreshGroup(queryClient, toValue(id))
      }
      throw error
    }
  }
}
