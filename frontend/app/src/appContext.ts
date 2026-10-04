import type { RouterHistory } from 'vue-router'
import { useToast } from '@scalenderplus/ui'
import { sessionQueryKey } from './composables/session'
import { createAppI18n, type Locale } from './i18n'
import { errorMessage } from './lib/errorMessages'
import { createAppQueryClient } from './queryClient'
import { createAppRouter } from './router'

/**
 * i18n, query client and router wired together (used by `main.ts` and the component tests):
 * an ended session clears the cached user and sends the visitor of a protected page to the
 * login with `?next=`; unhandled mutation errors become a toast with the code's message.
 */
export function createAppContext(options: { locale: Locale; history?: RouterHistory }) {
  const i18n = createAppI18n(options.locale)
  const t = i18n.global.t as (key: string, named?: Record<string, unknown>) => string
  const toast = useToast()

  // The hooks close over the router, created right below (the router guard needs the client).
  const queryClient = createAppQueryClient({
    onUnauthenticated: () => {
      queryClient.setQueryData(sessionQueryKey, null)
      const current = router.currentRoute.value
      if (current.meta.requiresAuth) {
        toast.info(t('auth.sessionEnded'))
        void router.replace({ name: 'login', query: { next: current.fullPath } })
      }
    },
    onMutationError: (error) => toast.error(errorMessage(t, error)),
  })
  const router = createAppRouter({ history: options.history, queryClient })
  return { i18n, t, toast, queryClient, router }
}
