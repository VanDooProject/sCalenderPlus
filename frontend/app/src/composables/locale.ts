import { useI18n } from 'vue-i18n'
import { persistLocale, supportedLocales, type Locale } from '@/i18n'

export function isSupportedLocale(value: unknown): value is Locale {
  return typeof value === 'string' && (supportedLocales as readonly string[]).includes(value)
}

/** Switches the UI language, remembers it and announces it to assistive technology. */
export function useLocaleSwitch() {
  const { locale } = useI18n()
  function setLocale(value: string) {
    if (!isSupportedLocale(value)) return
    locale.value = value
    persistLocale(value)
    document.documentElement.lang = value
  }
  return { locale, setLocale }
}
