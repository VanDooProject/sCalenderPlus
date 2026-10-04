import {
  createRouter,
  createWebHistory,
  type RouteLocationNormalized,
  type RouterHistory,
} from 'vue-router'
import type { QueryClient } from '@tanstack/vue-query'
import { sessionQuery } from '@/composables/session'
import { safeNext } from '@/lib/redirect'

declare module 'vue-router' {
  interface RouteMeta {
    /** i18n key of the page title. */
    title?: string
    /** Signed-out visitors are sent to `/login?next=…`. */
    requiresAuth?: boolean
    /** Signed-in users are sent on (`next` or the calendar), e.g. login and register. */
    guestOnly?: boolean
  }
}

export interface AppRouterOptions {
  history?: RouterHistory
  queryClient: QueryClient
}

export const homeRoute = { name: 'calendar' } as const

/** Every page is its own chunk; layouts too (signed-out visitors never load the shell). */
export function createAppRouter({ history = createWebHistory(), queryClient }: AppRouterOptions) {
  const router = createRouter({
    history,
    routes: [
      {
        path: '/',
        component: () => import('./layouts/AppShell.vue'),
        meta: { requiresAuth: true },
        children: [
          { path: '', redirect: homeRoute },
          {
            path: 'calendar',
            name: 'calendar',
            component: () => import('./views/calendar/CalendarView.vue'),
            meta: { title: 'nav.calendar' },
          },
          {
            path: 'groups',
            name: 'groups',
            component: () => import('./views/groups/GroupsView.vue'),
            meta: { title: 'nav.groups' },
          },
          {
            path: 'settings',
            component: () => import('./layouts/SettingsLayout.vue'),
            children: [
              { path: '', name: 'settings', redirect: { name: 'settings-profile' } },
              {
                path: 'profile',
                name: 'settings-profile',
                component: () => import('./views/settings/ProfileView.vue'),
                meta: { title: 'settings.profile.title' },
              },
              {
                path: 'security',
                name: 'settings-security',
                component: () => import('./views/settings/SecurityView.vue'),
                meta: { title: 'settings.security.title' },
              },
            ],
          },
        ],
      },
      {
        path: '/',
        component: () => import('./layouts/AuthLayout.vue'),
        children: [
          {
            path: 'login',
            name: 'login',
            component: () => import('./views/auth/LoginView.vue'),
            meta: { title: 'auth.login.title', guestOnly: true },
          },
          {
            path: 'register',
            name: 'register',
            component: () => import('./views/auth/RegisterView.vue'),
            meta: { title: 'auth.register.title', guestOnly: true },
          },
          {
            path: 'forgot-password',
            name: 'forgot-password',
            component: () => import('./views/auth/ForgotPasswordView.vue'),
            meta: { title: 'auth.forgot.title' },
          },
          {
            path: 'reset-password',
            name: 'reset-password',
            component: () => import('./views/auth/ResetPasswordView.vue'),
            meta: { title: 'auth.reset.title' },
          },
          {
            path: 'verify-email',
            name: 'verify-email',
            component: () => import('./views/auth/VerifyEmailView.vue'),
            meta: { title: 'auth.verify.title' },
          },
          {
            path: 'invite',
            name: 'invite',
            component: () => import('./views/invite/InviteView.vue'),
            meta: { title: 'invite.title' },
          },
        ],
      },
      {
        path: '/:pathMatch(.*)*',
        name: 'not-found',
        component: () => import('./views/NotFoundView.vue'),
        meta: { title: 'notFound.title' },
      },
    ],
  })

  router.beforeEach(async (to: RouteLocationNormalized) => {
    if (!to.meta.requiresAuth && !to.meta.guestOnly) return true
    let signedIn: boolean
    try {
      signedIn = (await queryClient.ensureQueryData(sessionQuery())) !== null
    } catch {
      // The api is unreachable: show the page; the shell renders the error with a retry.
      return true
    }
    if (to.meta.requiresAuth && !signedIn) {
      // The home page is where a login leads anyway: keep the URL clean.
      const home = router.resolve(homeRoute).fullPath
      return { name: 'login', query: to.fullPath === home ? {} : { next: to.fullPath } }
    }
    if (to.meta.guestOnly && signedIn) {
      return safeNext(to.query.next) ?? homeRoute
    }
    return true
  })

  return router
}
