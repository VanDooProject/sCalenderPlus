import { MutationCache, QueryCache, QueryClient } from '@tanstack/vue-query'
import { ApiError } from '@/lib/apiError'

export interface QueryErrorHooks {
  /** A request answered `401 unauthenticated`: the session ended (expired, revoked elsewhere). */
  onUnauthenticated?: () => void
  /** A mutation failed and nobody handles it locally (`meta: { handlesErrors: true }` opts out). */
  onMutationError?: (error: unknown) => void
}

function endsSession(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401 && error.code === 'unauthenticated'
}

/**
 * Global server-state policy: queries render their own loading/error states; an ended session is
 * handled once, centrally; unhandled mutation errors become a toast with the i18n text of the
 * problem code. 4xx answers are not retried (they will not change).
 */
export function createAppQueryClient(hooks: QueryErrorHooks = {}) {
  return new QueryClient({
    queryCache: new QueryCache({
      onError: (error) => {
        if (endsSession(error)) hooks.onUnauthenticated?.()
      },
    }),
    mutationCache: new MutationCache({
      onError: (error, _variables, _context, mutation) => {
        if (endsSession(error)) hooks.onUnauthenticated?.()
        if (mutation.meta?.handlesErrors === true) return
        hooks.onMutationError?.(error)
      },
    }),
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: false,
        retry: (failureCount, error) =>
          !(error instanceof ApiError && error.status < 500) && failureCount < 2,
      },
    },
  })
}
