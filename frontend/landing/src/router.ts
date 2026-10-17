import type { RouteRecordRaw } from 'vue-router'
import { legalPages, locales, notFoundPath, pathFor, type Page } from '@/lib/routes'

const components: Record<Page, () => Promise<unknown>> = {
  home: () => import('./pages/HomePage.vue'),
  bot: () => import('./pages/BotPage.vue'),
  ...Object.fromEntries(legalPages.map((doc) => [doc, () => import('./pages/LegalPage.vue')])),
} as Record<Page, () => Promise<unknown>>

const notFound = () => import('./pages/NotFoundPage.vue')

/** One static route per page and locale (meta: `locale`, `page`); see src/lib/routes.ts. */
export const routes: RouteRecordRaw[] = [
  ...locales.flatMap((locale) =>
    (Object.keys(components) as Page[]).map((page): RouteRecordRaw => ({
      path: pathFor(locale, page),
      name: `${locale}-${page}`,
      component: components[page],
      meta: { locale, page },
    })),
  ),
  { path: notFoundPath, name: 'not-found', component: notFound, meta: { locale: 'en' } },
  { path: '/:pathMatch(.*)*', component: notFound, meta: { locale: 'en' } },
]
