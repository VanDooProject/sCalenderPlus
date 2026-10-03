import createClient, { type Client, type Middleware } from 'openapi-fetch'
import type { paths } from './schema'

/** Methods that change state; the api requires `X-Requested-With: scal` on them (CSRF, api.md §3). */
const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

const csrfHeader: Middleware = {
  onRequest({ request }) {
    if (UNSAFE_METHODS.has(request.method.toUpperCase())) {
      request.headers.set('X-Requested-With', 'scal')
    }
    return request
  },
}

export type ApiClient = Client<paths>

export interface ApiClientOptions {
  /**
   * Origin of the api. Defaults to the page's origin (same-origin deployment, `web` proxies the api).
   * Must be absolute outside the browser (Vitest/Node).
   */
  baseUrl?: string
}

/** Typed client for the paths of `backend/openapi/v1.json` (openapi-fetch). */
export function createApiClient(options: ApiClientOptions = {}): ApiClient {
  const client = createClient<paths>({
    baseUrl: options.baseUrl ?? globalThis.location?.origin ?? '',
    // Resolve `fetch` per call so mocks installed after client creation (MSW) still apply.
    fetch: (request) => globalThis.fetch(request),
  })
  client.use(csrfHeader)
  return client
}
