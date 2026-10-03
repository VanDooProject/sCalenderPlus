import { createApiClient } from '@scalenderplus/api-client'

/** Typed API client (same-origin; `web`/Vite proxy forward to the api). */
export const api = createApiClient()
