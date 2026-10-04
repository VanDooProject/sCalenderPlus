import { computed, ref } from 'vue'

export const themePreferences = ['light', 'dark', 'system'] as const
export type ThemePreference = (typeof themePreferences)[number]

/** Same key as `public/theme-init.js`, which applies the theme before the first paint. */
const storageKey = 'scal.theme'

function isPreference(value: unknown): value is ThemePreference {
  return typeof value === 'string' && (themePreferences as readonly string[]).includes(value)
}

function readPreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(storageKey)
    return isPreference(stored) ? stored : 'system'
  } catch {
    return 'system'
  }
}

const media =
  typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-color-scheme: dark)')
    : null

const preference = ref<ThemePreference>(readPreference())
const systemDark = ref(media?.matches ?? false)
media?.addEventListener('change', (event) => {
  systemDark.value = event.matches
  apply()
})

const resolved = computed<'light' | 'dark'>(() =>
  preference.value === 'system' ? (systemDark.value ? 'dark' : 'light') : preference.value,
)

function apply() {
  if (typeof document === 'undefined') return
  document.documentElement.classList.toggle('dark', resolved.value === 'dark')
}

function setPreference(value: ThemePreference) {
  preference.value = value
  try {
    if (value === 'system') localStorage.removeItem(storageKey)
    else localStorage.setItem(storageKey, value)
  } catch {
    // Storage unavailable: the choice lasts for this page only.
  }
  apply()
}

/** Light/dark/system theme; the choice is stored per browser. */
export function useTheme() {
  return {
    preference: computed({ get: () => preference.value, set: setPreference }),
    resolved,
    apply,
  }
}
