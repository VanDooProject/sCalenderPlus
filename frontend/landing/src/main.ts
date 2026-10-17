import { ViteSSG } from 'vite-ssg'
import '@fontsource-variable/inter/wght.css'
import './style.css'
import 'virtual:brand.css'
import App from './App.vue'
import { routes } from './router'

/**
 * Prerendered with vite-ssg (`pnpm build`): every path of src/lib/routes.ts becomes a static HTML
 * file; in the browser the same app hydrates it (pricing period switch, the demo's viewer switch).
 */
export const createApp = ViteSSG(App, {
  routes,
  scrollBehavior: (to, _from, saved) => saved ?? (to.hash ? { el: to.hash } : { top: 0 }),
})
