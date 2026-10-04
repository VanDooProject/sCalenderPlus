import type { components } from './schema'

type ErrorCode = components['schemas']['ErrorCode']

/**
 * Every `ErrorCode` of the OpenAPI document as a runtime value. `satisfies Record<ErrorCode, true>`
 * makes a code added to (or removed from) the contract fail `typecheck` until this map follows, and
 * the app's i18n test checks that every listed code has a message in each locale.
 */
const errorCodeSet = {
  bad_request: true,
  billing_owner_must_be_owner: true,
  billing_owner_transfer_required: true,
  calendar_frozen: true,
  conflict: true,
  csrf_header_missing: true,
  email_domain_not_allowed: true,
  email_not_verified: true,
  external_sharing_not_allowed: true,
  feature_not_in_plan: true,
  group_frozen: true,
  group_has_calendars: true,
  insufficient_permission: true,
  internal_error: true,
  invalid_credentials: true,
  invite_email_mismatch: true,
  last_owner: true,
  method_not_allowed: true,
  not_found: true,
  override_invalid: true,
  override_invalid_in_target: true,
  payload_too_large: true,
  permission_self_lockout: true,
  plan_limit_reached: true,
  precondition_failed: true,
  precondition_required: true,
  rate_limited: true,
  reauthentication_failed: true,
  recurrence_invalid: true,
  recurrence_not_supported: true,
  service_unavailable: true,
  time_zone_invalid: true,
  token_expired: true,
  token_invalid: true,
  two_factor_required: true,
  uid_conflict: true,
  unauthenticated: true,
  unsupported_media_type: true,
  validation_failed: true,
} as const satisfies Record<ErrorCode, true>

export const errorCodes = Object.keys(errorCodeSet) as ErrorCode[]

export function isErrorCode(value: unknown): value is ErrorCode {
  return typeof value === 'string' && Object.hasOwn(errorCodeSet, value)
}
