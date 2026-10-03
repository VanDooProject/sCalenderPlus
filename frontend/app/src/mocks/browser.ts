import { http, HttpResponse } from 'msw'
import { setupWorker } from 'msw/browser'
import { handlers } from '@scalenderplus/api-client/mocks'
import type { AppConfig } from '@/config'

/** `/config.json` is served by `web` (not part of the API); in mock mode the badge shows "mock". */
const mockConfig: AppConfig = { environment: 'mock' }

export const worker = setupWorker(
  http.get('/config.json', () => HttpResponse.json(mockConfig)),
  ...handlers,
)
