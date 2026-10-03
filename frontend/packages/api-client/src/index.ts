/**
 * sCalenderPlus API client: types generated from `backend/openapi/v1.json` by openapi-typescript
 * (`pnpm --filter @scalenderplus/api-client generate`) and a typed openapi-fetch client.
 * See docs/architecture/api.md §6.
 */
import type { components } from './schema'

export { createApiClient, type ApiClient, type ApiClientOptions } from './client'
export type { components, operations, paths } from './schema'

export type HealthResponse = components['schemas']['HealthResponse']

/** Stable error `code` of a problem response (api.md §2); map to i18n messages, never to `title`. */
export type ErrorCode = components['schemas']['ErrorCode']

/** RFC 9457 problem details body of every error response (`application/problem+json`). */
export type ProblemDetails = components['schemas']['ProblemDetails']

/** Versioned REST base path; same-origin (proxied by `web`). */
export const API_BASE_PATH = '/api/v1'
