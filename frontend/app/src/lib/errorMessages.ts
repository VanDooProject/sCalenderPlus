import type { ErrorCode } from '@scalenderplus/api-client'
import type { ComposerTranslation } from 'vue-i18n'
import { ApiError, NetworkError } from './apiError'

/** Message key of an error code: one per `ErrorCode` (checked against the schema in i18n). */
export function errorCodeKey(code: ErrorCode): `errors.code.${ErrorCode}` {
  return `errors.code.${code}`
}

type Translate = ComposerTranslation | ((key: string, named?: Record<string, unknown>) => string)

/**
 * A wait as words in the page language: seconds below 90 s, then minutes, then hours (rounded up),
 * e.g. `42 seconds`, `60 minutes`, `2 Stunden`.
 */
export function formatWait(
  seconds: number,
  locale = document.documentElement.lang || 'en',
): string {
  const [value, unit] =
    seconds < 90
      ? [Math.max(1, Math.ceil(seconds)), 'second']
      : seconds < 90 * 60
        ? [Math.ceil(seconds / 60), 'minute']
        : [Math.ceil(seconds / 3600), 'hour']
  return new Intl.NumberFormat(locale, { style: 'unit', unit, unitDisplay: 'long' }).format(value)
}

/**
 * A user-facing sentence for any thrown value: the i18n text of the problem `code` (never the api's
 * English `title`/`detail`), with the `Retry-After` wait for `rate_limited`.
 */
export function errorMessage(t: Translate, error: unknown): string {
  const translate = t as (key: string, named?: Record<string, unknown>) => string
  if (error instanceof NetworkError) return translate('errors.network')
  if (error instanceof ApiError) {
    if (error.code === 'rate_limited' && error.retryAfter !== null) {
      return translate('errors.rateLimitedRetry', { wait: formatWait(error.retryAfter) })
    }
    return translate(errorCodeKey(error.code))
  }
  return translate('errors.unexpected')
}

export function isApiError(error: unknown, ...codes: ErrorCode[]): error is ApiError {
  return error instanceof ApiError && (codes.length === 0 || codes.includes(error.code))
}
