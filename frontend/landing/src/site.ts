import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { catalogue, withOverrides } from '@scalenderplus/plans'
import { edition } from '@edition'
import de from '@/locales/de.json'
import en from '@/locales/en.json'
import {
  format,
  lookup,
  mergeMessages,
  translate,
  type Message,
  type Messages,
  type Params,
} from '@/lib/messages'
import { defaultLocale, pathFor, type Locale, type Page } from '@/lib/routes'

/** Build-time settings (vite.config.ts) and the build's edition. */
export const site = __SITE__
export { edition }

/** Base messages with the edition's overlay. */
export const messages: Record<Locale, Messages> = {
  en: mergeMessages(en as Messages, edition.messages.en as Messages),
  de: mergeMessages(de as Messages, edition.messages.de as Messages),
}

/** Plan numbers of this edition (plans.json with the edition's overrides). */
export const plans = withOverrides(catalogue, edition.plans)

/** Params every message may use. */
const globals = (): Params => ({
  name: site.brandName,
  trialDays: plans.trialDays,
  bot: site.botUserAgent.split('/')[0]!,
})

export function createT(locale: Locale) {
  const t = (key: string, params?: Params) =>
    translate(messages[locale], key, { ...globals(), ...params })
  const tm = <T = Message>(key: string) => lookup(messages[locale], key) as T
  return { t, tm }
}

/** The page being rendered: its locale and page id (route meta), translations and links. */
export function usePage() {
  const route = useRoute()
  const locale = computed<Locale>(() => (route.meta.locale as Locale | undefined) ?? defaultLocale)
  const page = computed<Page | null>(() => (route.meta.page as Page | undefined) ?? null)
  const t = (key: string, params?: Params) => createT(locale.value).t(key, params)
  const tm = <T = Message>(key: string) => createT(locale.value).tm<T>(key)
  /** Fills a message taken with `tm` (e.g. an FAQ item). */
  const fmt = (text: string, params?: Params) => format(text, { ...globals(), ...params })
  const link = (target: Page) => pathFor(locale.value, target)
  return { locale, page, t, tm, fmt, link }
}

/** Absolute link into the web app. */
export const appLink = (path: string) => `${site.appUrl}${path}`
