import { createApp } from 'vue'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import App from './App.vue'
import { configKey, loadConfig } from './config'
import { createAppI18n } from './i18n'
import { createAppRouter } from './router'
import './style.css'

/** Mock mode (`vite --mode mock`): start MSW before the first request. Tree-shaken from builds. */
async function enableMocking() {
  if (import.meta.env.MODE !== 'mock') return
  const { worker } = await import('./mocks/browser')
  await worker.start({ onUnhandledFrame: 'bypass', quiet: true })
}

async function bootstrap() {
  await enableMocking()
  const config = await loadConfig()
  const queryClient = new QueryClient({
    defaultOptions: { queries: { staleTime: 30_000, retry: 1 } },
  })

  createApp(App)
    .provide(configKey, config)
    .use(createAppI18n())
    .use(createAppRouter())
    .use(VueQueryPlugin, { queryClient })
    .mount('#app')
}

void bootstrap()
