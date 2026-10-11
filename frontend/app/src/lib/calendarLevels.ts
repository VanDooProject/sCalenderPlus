import { errorMessage, isApiError } from './errorMessages'

/**
 * Calendar levels of permissions.md §3 (`Core/Permissions` on the server), used only to hide or
 * disable what the user cannot do. The api decides: every action still handles its errors.
 */
export type CalendarLevel =
  'none' | 'free_busy' | 'read' | 'contribute' | 'edit' | 'manage' | 'owner'

export const calendarLevels: readonly CalendarLevel[] = [
  'none',
  'free_busy',
  'read',
  'contribute',
  'edit',
  'manage',
  'owner',
]

const rank: Record<CalendarLevel, number> = {
  none: 0,
  free_busy: 1,
  read: 2,
  contribute: 3,
  edit: 4,
  manage: 5,
  owner: 6,
}

/** The level as a known level; unknown values (a newer api) count as `none`. */
export function asLevel(value: string | null | undefined): CalendarLevel {
  return typeof value === 'string' && value in rank ? (value as CalendarLevel) : 'none'
}

export function levelAtLeast(level: string, minimum: CalendarLevel): boolean {
  return rank[asLevel(level)] >= rank[minimum]
}

export function isLowering(from: string, to: string): boolean {
  return rank[asLevel(to)] < rank[asLevel(from)]
}

/** Settings, grants and role defaults need `manage`. */
export const canManageCalendar = (myLevel: string) => levelAtLeast(myLevel, 'manage')
/** Deleting (and transferring) is the owner's. */
export const canDeleteCalendar = (myLevel: string) => asLevel(myLevel) === 'owner'

/** Levels a grant may carry: `free_busy` … `manage`, never above the actor's level. */
export function grantableLevels(myLevel: string): CalendarLevel[] {
  const max = Math.min(rank[asLevel(myLevel)], rank.manage)
  return calendarLevels.filter((l) => rank[l] >= rank.free_busy && rank[l] <= max)
}

/** Levels a group role default may carry: `none` … `manage`, never above the actor's level. */
export function roleDefaultLevels(myLevel: string): CalendarLevel[] {
  const max = Math.min(rank[asLevel(myLevel)], rank.manage)
  return calendarLevels.filter((l) => rank[l] <= max)
}

/**
 * Whether a change is refused while the calendar is frozen: new grants and raising anything are;
 * lowering and removing (taking access away) never wait for a plan upgrade.
 */
export function blockedByFrozen(frozen: boolean, from: string | null, to: string | null): boolean {
  if (!frozen) return false
  if (to === null) return false
  if (from === null) return true
  return !isLowering(from, to)
}

type Translate = (key: string, named?: Record<string, unknown>) => string

/**
 * A sentence for an error of a calendar settings action: self lockout, frozen calendar and plan
 * limits are explained in terms of calendars; anything else falls back to the code's message.
 */
export function calendarErrorMessage(t: Translate, error: unknown): string {
  if (isApiError(error, 'permission_self_lockout')) return t('calendarSettings.errors.selfLockout')
  if (isApiError(error, 'calendar_frozen')) return t('calendarSettings.errors.frozen')
  if (isApiError(error, 'plan_limit_reached')) return t('calendarSettings.errors.planLimit')
  if (isApiError(error, 'precondition_failed')) return t('calendarSettings.errors.changed')
  if (isApiError(error, 'insufficient_permission')) return t('calendarSettings.errors.forbidden')
  if (isApiError(error, 'conflict')) return t('calendarSettings.errors.grantExists')
  return errorMessage(t, error)
}
