import { readonly, ref } from 'vue'

/**
 * The service worker of the installable app (vite.config.ts `VitePWA`): registered in production
 * builds only, so dev and mock mode (MSW) never get one. A new version installs in the background
 * and waits; `updateAvailable` turns true and UpdatePrompt.vue offers a reload, which activates it.
 */
const available = ref(false)
let activate: (() => Promise<void>) | null = null

export const updateAvailable = readonly(available)

/** How often an open app asks the server for a new version. */
const updateCheckInterval = 60 * 60 * 1000

export async function registerServiceWorker(): Promise<void> {
  if (!import.meta.env.PROD || !('serviceWorker' in navigator)) return
  let registerSW: typeof import('virtual:pwa-register').registerSW
  try {
    ;({ registerSW } = await import('virtual:pwa-register'))
  } catch {
    // The chunk didn't load (e.g. offline before the worker was installed): next visit.
    return
  }
  const update = registerSW({
    immediate: true,
    onNeedRefresh: () => {
      available.value = true
    },
    onRegisteredSW: (_url, registration) => {
      if (!registration) return
      setInterval(() => {
        if (navigator.onLine) void registration.update().catch(() => undefined)
      }, updateCheckInterval)
    },
  })
  activate = () => update(true)
}

/** Activates the waiting version; the page reloads once it has taken over. */
export async function applyUpdate(): Promise<void> {
  if (activate) await activate()
  else window.location.reload()
}

/** Hides the prompt until the next new version (the waiting one activates on the next visit). */
export function dismissUpdate(): void {
  available.value = false
}

/** Test hook: shows the prompt as if a new version were waiting. */
export function setUpdateAvailableForTests(value: boolean, onApply?: () => Promise<void>): void {
  available.value = value
  activate = onApply ?? null
}
