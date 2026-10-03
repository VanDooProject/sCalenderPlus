import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createMemoryHistory } from 'vue-router'
import App from '@/App.vue'
import { configKey, type AppConfig } from '@/config'
import { createAppI18n } from '@/i18n'
import { createAppRouter } from '@/router'

async function mountApp(config: AppConfig = { environment: 'staging' }) {
  const router = createAppRouter(createMemoryHistory())
  await router.push('/')
  await router.isReady()
  const wrapper = mount(App, {
    global: {
      plugins: [createAppI18n('en'), router, [VueQueryPlugin, { queryClient: new QueryClient() }]],
      provide: { [configKey as symbol]: config },
    },
  })
  await flushPromises()
  return wrapper
}

describe('App shell', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders the home page with a reachable API', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response('{"status":"Healthy"}', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = await mountApp()

    expect(wrapper.get('[data-testid="app-title"]').text()).toBe('sCalenderPlus')
    expect(wrapper.text()).toContain('Welcome')
    expect(wrapper.get('[data-testid="environment-badge"]').text()).toBe('Environment: staging')
    expect(wrapper.get('[data-testid="api-status"]').text()).toBe('reachable')
    expect(fetchMock).toHaveBeenCalledWith('/health/live', expect.anything())
  })

  it('shows the API as unreachable when the health check fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 503 })))

    const wrapper = await mountApp()

    expect(wrapper.get('[data-testid="api-status"]').text()).toBe('unreachable')
  })

  it('hides the environment badge in production', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 200 })))

    const wrapper = await mountApp({ environment: 'production' })

    expect(wrapper.find('[data-testid="environment-badge"]').exists()).toBe(false)
  })

  it('switches the language to German and remembers the choice', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 200 })))
    const wrapper = await mountApp()

    await wrapper.get('[data-testid="locale-switcher"]').setValue('de')

    expect(wrapper.text()).toContain('Willkommen')
    expect(wrapper.get('[data-testid="environment-badge"]').text()).toBe('Umgebung: staging')
    expect(localStorage.getItem('scal.locale')).toBe('de')
    expect(document.documentElement.lang).toBe('de')
  })
})
