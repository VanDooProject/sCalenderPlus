import { computed } from 'vue'
import {
  queryOptions,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from '@tanstack/vue-query'
import type { MeResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import { ApiError, call } from '@/lib/apiError'

/** The signed-in user with the `ETag` of `GET /me` (the `If-Match` of profile changes). */
export interface Session {
  user: MeResponse
  /** Null when the user came from a login response; refetched before it is needed. */
  etag: string | null
}

export const sessionQueryKey = ['me'] as const

/** `GET /me`: the session, or `null` when signed out (401 is an answer here, not an error). */
export async function fetchSession(): Promise<Session | null> {
  try {
    const { data, response } = await call(api.GET('/api/v1/me'))
    return { user: data, etag: response.headers.get('ETag') }
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null
    throw error
  }
}

export function sessionQuery() {
  return queryOptions({
    queryKey: sessionQueryKey,
    queryFn: fetchSession,
    staleTime: 5 * 60_000,
    retry: (failureCount, error) => !(error instanceof ApiError) && failureCount < 2,
  })
}

/** Stores a user from a login/profile response; a missing ETag is fetched in the background. */
export function setSessionUser(queryClient: QueryClient, user: MeResponse, etag: string | null) {
  const session: Session = { user, etag }
  queryClient.setQueryData(sessionQueryKey, () => session)
  if (!etag) void queryClient.invalidateQueries({ queryKey: sessionQueryKey })
}

export function useSession() {
  const query = useQuery(sessionQuery())
  return {
    query,
    session: computed(() => query.data.value ?? null),
    user: computed(() => query.data.value?.user ?? null),
  }
}

/** `POST /auth/logout`, then forget every cached server state of the user. */
export function useLogout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      try {
        await call(api.POST('/api/v1/auth/logout'))
      } catch (error) {
        // Already signed out (expired session): the goal is reached.
        if (!(error instanceof ApiError && error.status === 401)) throw error
      }
    },
    onSuccess: () => {
      queryClient.clear()
      queryClient.setQueryData(sessionQueryKey, null)
    },
  })
}
