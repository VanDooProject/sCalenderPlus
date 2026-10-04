import { errorMessage, isApiError } from './errorMessages'

type Translate = (key: string, named?: Record<string, unknown>) => string

/** What the user tried, so the explanation can say what to do next. */
export type GroupAction =
  | { kind: 'leave' }
  | { kind: 'remove'; name: string }
  | { kind: 'role'; name: string; self: boolean }
  | { kind: 'transfer'; name: string }
  | { kind: 'other' }

/**
 * A sentence for an error of a group action that explains the group rules (last owner, billing
 * owner, frozen group) in terms of the action; anything else falls back to the code's message.
 */
export function groupErrorMessage(t: Translate, error: unknown, action: GroupAction): string {
  const name = 'name' in action ? action.name : ''
  const self = action.kind === 'leave' || (action.kind === 'role' && action.self)
  if (isApiError(error, 'last_owner')) {
    if (action.kind === 'leave') return t('groups.errors.lastOwnerLeave')
    return self ? t('groups.errors.lastOwnerDemoteSelf') : t('groups.errors.lastOwner', { name })
  }
  if (isApiError(error, 'billing_owner_transfer_required')) {
    if (action.kind === 'leave') return t('groups.errors.billingLeave')
    return self ? t('groups.errors.billingDemoteSelf') : t('groups.errors.billing', { name })
  }
  if (isApiError(error, 'billing_owner_must_be_owner')) {
    return t('groups.errors.billingMustBeOwner', { name })
  }
  if (isApiError(error, 'group_frozen')) return t('groups.errors.frozen')
  if (isApiError(error, 'group_has_calendars')) return t('groups.errors.hasCalendars')
  if (isApiError(error, 'insufficient_permission')) return t('groups.errors.forbidden')
  return errorMessage(t, error)
}
