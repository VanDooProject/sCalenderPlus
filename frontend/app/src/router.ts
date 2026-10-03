import { createRouter, createWebHistory, type RouterHistory } from 'vue-router'
import HomeView from './views/HomeView.vue'

export function createAppRouter(history: RouterHistory = createWebHistory()) {
  return createRouter({
    history,
    routes: [
      { path: '/', name: 'home', component: HomeView },
      {
        path: '/:pathMatch(.*)*',
        name: 'not-found',
        component: () => import('./views/NotFoundView.vue'),
      },
    ],
  })
}
