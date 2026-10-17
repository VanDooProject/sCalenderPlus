/**
 * Pages and their static paths per locale: English at the root (`/`, `/bot`), German under `/de`
 * (`/de`, `/de/bot`). Every page exists in every locale, so each one links its translations
 * (hreflang) and the sitemap lists them all. Shared by the app (router) and vite.config.ts (prerender
 * list, sitemap.xml, robots.txt).
 */
export const locales = ['en', 'de'] as const
export type Locale = (typeof locales)[number]
export const defaultLocale: Locale = 'en'

export const pages = ['home', 'terms', 'privacy', 'imprint', 'dpa', 'bot'] as const
export type Page = (typeof pages)[number]

/** The legal documents (built-in templates unless the operator links its own, siteConfig.ts). */
export const legalPages = ['imprint', 'privacy', 'terms', 'dpa'] as const
export type LegalPage = (typeof legalPages)[number]

const pagePaths: Record<Page, string> = {
  home: '',
  terms: 'legal/terms',
  privacy: 'legal/privacy',
  imprint: 'legal/imprint',
  dpa: 'legal/dpa',
  bot: 'bot',
}

/** The path of a page in a locale, without trailing slash (except the root). */
export function pathFor(locale: Locale, page: Page): string {
  const prefix = locale === defaultLocale ? '' : `/${locale}`
  const path = pagePaths[page]
  if (path === '') return prefix === '' ? '/' : prefix
  return `${prefix}/${path}`
}

/** The prerendered not-found page (served by Caddy for unknown paths with status 404). */
export const notFoundPath = '/404'

/** Every prerendered path. */
export const allPaths = (): string[] => [
  ...locales.flatMap((locale) => pages.map((page) => pathFor(locale, page))),
  notFoundPath,
]

/** BCP 47 tag and Open Graph locale of each locale. */
export const localeTags: Record<Locale, { hreflang: string; og: string; name: string }> = {
  en: { hreflang: 'en', og: 'en_US', name: 'English' },
  de: { hreflang: 'de', og: 'de_DE', name: 'Deutsch' },
}
