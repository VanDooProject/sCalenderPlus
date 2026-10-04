import { isErrorCode, type ErrorCode, type ProblemDetails } from '@scalenderplus/api-client'

/** A problem response of the api (RFC 9457, `code` from the contract). */
export class ApiError extends Error {
  readonly status: number
  readonly problem: Partial<ProblemDetails>
  /** Seconds from `Retry-After` (429/503), if sent. */
  readonly retryAfter: number | null

  constructor(status: number, problem: unknown, retryAfter: number | null = null) {
    const body = (typeof problem === 'object' && problem !== null ? problem : {}) as Partial<
      ProblemDetails & { errors: unknown }
    >
    super(body.detail ?? body.title ?? `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = body as Partial<ProblemDetails>
    this.retryAfter = retryAfter
  }

  /** The stable error code; derived from the status when the body has none (e.g. a proxy 502). */
  get code(): ErrorCode {
    if (isErrorCode(this.problem.code)) return this.problem.code
    if (this.status === 401) return 'unauthenticated'
    if (this.status === 404) return 'not_found'
    if (this.status === 429) return 'rate_limited'
    if (this.status === 503 || this.status === 502 || this.status === 504)
      return 'service_unavailable'
    if (this.status >= 500) return 'internal_error'
    return 'bad_request'
  }

  /** Validation messages by camelCase field path (`errors` of `validation_failed` and 422 codes). */
  get fieldErrors(): Record<string, string[]> {
    const errors = (this.problem as { errors?: unknown }).errors
    if (typeof errors !== 'object' || errors === null) return {}
    return Object.fromEntries(
      Object.entries(errors as Record<string, unknown>)
        .filter((entry): entry is [string, string[]] => Array.isArray(entry[1]))
        .map(([key, messages]) => [key, messages.filter((m) => typeof m === 'string')]),
    )
  }
}

/** The request never got a response (offline, DNS, CORS, aborted). */
export class NetworkError extends Error {
  constructor(cause: unknown) {
    super('Network request failed', { cause })
    this.name = 'NetworkError'
  }
}

/** `Retry-After` as seconds: delta-seconds or an HTTP date. */
export function parseRetryAfter(value: string | null, now: number = Date.now()): number | null {
  if (!value) return null
  if (/^\d+$/.test(value.trim())) return Number(value.trim())
  const date = Date.parse(value)
  return Number.isNaN(date) ? null : Math.max(0, Math.ceil((date - now) / 1000))
}

interface FetchResult<D> {
  data?: D
  error?: unknown
  response: Response
}

/**
 * Awaits an openapi-fetch call and throws {@link ApiError} for problem responses and
 * {@link NetworkError} when there was no response; returns the data and the raw response (headers).
 */
export async function call<D>(
  request: Promise<FetchResult<D>>,
): Promise<{ data: D; response: Response }> {
  let result: FetchResult<D>
  try {
    result = await request
  } catch (error) {
    throw new NetworkError(error)
  }
  const { response } = result
  if (!response.ok) {
    throw new ApiError(
      response.status,
      result.error,
      parseRetryAfter(response.headers.get('Retry-After')),
    )
  }
  return { data: result.data as D, response }
}
