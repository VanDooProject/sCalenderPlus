import {
  createRouter,
  createWebHistory,
  type RouteLocationNormalized,
  type RouterHistory,
} from 'vue-router'
import type { QueryClient } from '@tanstack/vue-query'
import { sessionQuery } from '@/composables/session'
import { legalDocuments, legalRouteName } from '@/lib/legal'
import { safeNext } from '@/lib/redirect'

declare module 'vue-router' {
  interface RouteMeta {
    /** i18n key of the page title. */
    title?: string
    /** Signed-out visitors are sent to `/login?next=…`. */
    requiresAuth?: boolean
    /** Signed-in users are sent on (`next` or the calendar), e.g. login and register. */
    guestOnly?: boolean
    /** AuthLayout: a wide reading column instead of the narrow form card (legal pages). */
    wide?: boolean
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
            path: 'calendars/:calendarId/settings',
            name: 'calendar-settings',
            component: () => import('./views/calendar/CalendarSettingsView.vue'),
            meta: { title: 'calendar.settings' },
          },
          {
            path: 'groups',
            name: 'groups',
            component: () => import('./views/groups/GroupsView.vue'),
            meta: { title: 'nav.groups' },
          },
          {
            path: 'groups/:groupId',
            component: () => import('./views/groups/GroupView.vue'),
            children: [
              {
                path: '',
                name: 'group',
                redirect: (to) => ({ name: 'group-members', params: to.params }),
              },
              {
                path: 'members',
                name: 'group-members',
                component: () => import('./views/groups/GroupMembersView.vue'),
                meta: { title: 'groups.tabs.members' },
              },
              {
                path: 'invites',
                name: 'group-invites',
                component: () => import('./views/groups/GroupInvitesView.vue'),
                meta: { title: 'groups.tabs.invites' },
              },
              {
                path: 'settings',
                name: 'group-settings',
                component: () => import('./views/groups/GroupSettingsView.vue'),
                meta: { title: 'groups.tabs.settings' },
              },
            ],
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
          // Public for everyone, signed in or not.
          ...legalDocuments.map((doc) => ({
            path: `legal/${doc}`,
            name: legalRouteName(doc),
            component: () => import('./views/legal/LegalView.vue'),
            props: { doc },
            meta: { title: `legal.pages.${doc}.title`, wide: true },
          })),
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
