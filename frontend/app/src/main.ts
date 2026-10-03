import { createApp } from 'vue'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import App from './App.vue'
import { configKey, loadConfig } from './config'
import { createAppI18n } from './i18n'
import { createAppRouter } from './router'
import './style.css'

async function bootstrap() {
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
