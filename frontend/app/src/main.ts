import { createApp } from 'vue'
import { VueQueryPlugin } from '@tanstack/vue-query'
import '@fontsource-variable/inter/wght.css'
import App from './App.vue'
import { createAppContext } from './appContext'
import { useTheme } from './composables/theme'
import { configKey, loadConfig } from './config'
import { detectLocale } from './i18n'
import './style.css'

/** Mock mode (`vite --mode mock`): start MSW before the first request. Tree-shaken from builds. */
async function enableMocking() {
  if (import.meta.env.MODE !== 'mock') return
  const { startMockWorker } = await import('./mocks/browser')
  await startMockWorker()
}

async function bootstrap() {
  await enableMocking()
  const config = await loadConfig()
  useTheme().apply()

  const locale = detectLocale()
  // index.html says `en`; announce the detected locale to assistive technology from the start.
  document.documentElement.lang = locale
  const { i18n, t, toast, queryClient, router } = createAppContext({ locale })

  const app = createApp(App)
  app.config.errorHandler = (error) => {
    console.error(error)
    toast.error(t('errors.unexpected'))
  }
  app
    .provide(configKey, config)
    .use(i18n)
    .use(router)
    .use(VueQueryPlugin, { queryClient })
    .mount('#app')
}

void bootstrap()
