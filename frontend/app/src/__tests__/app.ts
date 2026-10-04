import { flushPromises, mount } from '@vue/test-utils'
import { VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory } from 'vue-router'
import { useToast } from '@scalenderplus/ui'
import App from '@/App.vue'
import { configKey, type AppConfig } from '@/config'
import { createAppContext } from '@/appContext'
import type { Locale } from '@/i18n'

const mounted: { unmount: () => void }[] = []

export interface MountOptions {
  config?: AppConfig
  locale?: Locale
}

/**
 * Mounts the whole app (router, i18n, query client) at `path` against the MSW handlers, attached
 * to the document so portals (dialogs, menus, toasts) render.
 */
export async function mountApp(path: string, options: MountOptions = {}) {
  const { i18n, router, queryClient } = createAppContext({
    locale: options.locale ?? 'en',
    history: createMemoryHistory(),
  })
  await router.push(path)
  await router.isReady()
  const wrapper = mount(App, {
    attachTo: document.body,
    global: {
      plugins: [i18n, router, [VueQueryPlugin, { queryClient }]],
      provide: { [configKey as symbol]: options.config ?? { environment: 'test' } },
    },
  })
  mounted.push(wrapper)
  await settle()
  return { wrapper, router, queryClient, i18n }
}

/** Lets requests, lazy routes and re-renders finish. */
export async function settle(rounds = 8) {
  for (let i = 0; i < rounds; i++) {
    await flushPromises()
    await new Promise((resolve) => setTimeout(resolve, 5))
  }
}

/** Settles until `condition` holds (lazy route chunks can take a while in a cold run). */
export async function until(condition: () => boolean, timeout = 5000) {
  const start = Date.now()
  while (!condition()) {
    if (Date.now() - start > timeout) throw new Error('Condition not met in time')
    await settle(1)
  }
  await settle()
}

export function byTestId(id: string): HTMLElement {
  const element = document.querySelector<HTMLElement>(`[data-testid="${id}"]`)
  if (!element) throw new Error(`No element with data-testid="${id}"`)
  return element
}

export function queryTestId(id: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-testid="${id}"]`)
}

/** Types into an input like a user (value + input event). */
export async function type(id: string, value: string) {
  const input = byTestId(id) as HTMLInputElement
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
}

export async function click(id: string) {
  byTestId(id).click()
  await settle()
}

export async function submit(formId: string) {
  byTestId(formId).dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await settle()
}

export function resetDom() {
  mounted.splice(0).forEach((wrapper) => wrapper.unmount())
  document.body.innerHTML = ''
  useToast().clear()
  localStorage.clear()
}
