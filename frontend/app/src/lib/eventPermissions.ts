import type { ExplainStepResponse, OverrideResponse } from '@scalenderplus/api-client'
import { ApiError } from './apiError'

/**
 * Event permission overrides (permissions.md §3, §4.4): principals, the levels an override may
 * carry, violations of a refused `PUT /events/{id}/overrides` and the explainer's steps as
 * sentences. The api decides; this only presents.
 */
export type OverrideLevel = 'none' | 'free_busy' | 'read' | 'edit'
export const overrideLevels: readonly OverrideLevel[] = ['none', 'free_busy', 'read', 'edit']

export type PrincipalType = 'user' | 'group' | 'anonymous' | 'everyone'

export interface Principal {
  type: PrincipalType | string
  id?: string | null
  minRole?: string | null
}

/** An entry being edited: the principal, its display name and the chosen level. */
export interface OverrideRow {
  principal: Principal
  name: string | null
  level: string
}

type Translate = (key: string, named?: Record<string, unknown>) => string

/** Stable key of a principal (`user:{id}`, `group:{id}`, `everyone`) — one entry per principal. */
export function principalKey(principal: Principal): string {
  if (principal.type === 'user' || principal.type === 'group') {
    return `${principal.type}:${principal.id ?? ''}`
  }
  return principal.type
}

const order: Record<string, number> = { user: 0, group: 1, anonymous: 2, everyone: 3 }

/** Rows in the api's order: users, groups, anyone with the link, everyone; then by name. */
export function sortRows(rows: OverrideRow[]): OverrideRow[] {
  return [...rows].sort(
    (a, b) =>
      (order[a.principal.type] ?? 9) - (order[b.principal.type] ?? 9) ||
      (a.name ?? '').localeCompare(b.name ?? ''),
  )
}

export function rowsFromResponse(items: OverrideResponse[]): OverrideRow[] {
  return sortRows(
    items.map((o) => ({
      principal: { type: o.principal.type, id: o.principal.id, minRole: o.principal.minRole },
      name: o.principalName ?? null,
      level: o.level,
    })),
  )
}

/** The body of `PUT /events/{id}/overrides`. */
export function requestFromRows(rows: OverrideRow[]) {
  return {
    overrides: rows.map((r) => ({
      principal: {
        type: r.principal.type,
        ...(r.principal.id ? { id: r.principal.id } : {}),
        ...(r.principal.type === 'group' && r.principal.minRole
          ? { minRole: r.principal.minRole }
          : {}),
      },
      level: r.level,
    })),
  }
}

/** Plain-language label of an override level ("Hidden", "Busy only", "Can view", "Can edit"). */
export function levelLabel(t: Translate, level: string): string {
  return overrideLevels.includes(level as OverrideLevel) || level === 'manage'
    ? t(`permissions.levels.${level}`)
    : level
}

/** "Mia", "FC Lions (admins and above)", "Anyone with the link", "Everyone in this calendar". */
export function principalLabel(t: Translate, principal: Principal, name: string | null): string {
  switch (principal.type) {
    case 'everyone':
      return t('permissions.principal.everyone')
    case 'anonymous':
      return t('permissions.principal.anonymous')
    case 'group': {
      const group = name ?? t('permissions.principal.unknownGroup')
      const role = principal.minRole ?? 'viewer'
      return role === 'viewer'
        ? group
        : t('permissions.principal.groupMinRole', { group, role: t(`groups.roles.${role}`) })
    }
    default:
      return name ?? t('permissions.principal.unknownUser')
  }
}

/** The presets: "Private" (only me and managers) and "Busy only for everyone". */
export type Preset = 'private' | 'busy'

/** Applies a preset: sets the `everyone` entry, keeping explicit user and group entries. */
export function applyPreset(rows: OverrideRow[], preset: Preset): OverrideRow[] {
  const level = preset === 'private' ? 'none' : 'free_busy'
  const rest = rows.filter((r) => r.principal.type !== 'everyone')
  return sortRows([...rest, { principal: { type: 'everyone' }, name: null, level }])
}

/** One entry of `violations` (422 `override_invalid`, 403 `external_sharing_not_allowed`, 409). */
export interface Violation {
  principal: Principal
  principalName?: string | null
  level?: string | null
  reason: string
}

const errorCodesWithViolations = [
  'override_invalid',
  'external_sharing_not_allowed',
  'override_invalid_in_target',
]

/** The `violations` of a problem response, or an empty list. */
export function violationsOf(error: unknown): Violation[] {
  if (!(error instanceof ApiError) || !errorCodesWithViolations.includes(error.code)) return []
  const raw = (error.problem as { violations?: unknown }).violations
  if (!Array.isArray(raw)) return []
  return raw.filter(
    (v): v is Violation =>
      typeof v === 'object' &&
      v !== null &&
      typeof (v as Violation).reason === 'string' &&
      typeof (v as Violation).principal === 'object',
  )
}

/** Violations by row key (a principal's key); several reasons for one row are joined. */
export function violationsByRow(violations: Violation[]): Record<string, string[]> {
  const result: Record<string, string[]> = {}
  for (const v of violations) {
    const key = principalKey(v.principal ?? { type: 'everyone' })
    ;(result[key] ??= []).push(v.reason)
  }
  return result
}

const knownReasons = [
  'level_too_high',
  'duplicate_principal',
  'group_not_selectable',
  'user_not_selectable',
  'external_sharing',
  'removal_exposes_external_share',
  'no_override_rights_in_target',
]

/** A sentence for a violation reason; unknown reasons (a newer api) get a generic one. */
export function violationMessage(t: Translate, reason: string): string {
  return knownReasons.includes(reason)
    ? t(`permissions.violations.${reason}`)
    : t('permissions.violations.unknown')
}

// The explainer (api.md "Access explainer").

const stepKinds = [
  'share_link',
  'calendar_owner',
  'grant_matched',
  'group_role_default',
  'calendar_result',
  'inherited_from_series',
  'manager_floor',
  'creator_floor',
  'creator_floor_disabled',
  'creator_floor_lapsed',
  'base_level',
  'override_matched',
  'no_matching_override',
  'override_applied',
  'restrict_only',
  'override_cap',
  'link_ceiling',
  'result',
]

/** Calendar levels in words, for the steps that name one. */
function calendarLevelLabel(t: Translate, level: string | null | undefined): string {
  return level ? t(`calendarSettings.levels.${level}.label`) : ''
}

/** A step of the explainer as a sentence ("Mia created this event and keeps full control."). */
export function stepSentence(t: Translate, step: ExplainStepResponse): string {
  const kind = stepKinds.includes(step.kind) ? step.kind : 'unknown'
  const principal = step.principal
    ? principalLabel(t, step.principal, step.principalName ?? null)
    : ''
  return t(`permissions.steps.${kind}`, {
    principal,
    role: step.role ? t(`groups.roles.${step.role}`) : '',
    calendarLevel: calendarLevelLabel(t, step.calendarLevel),
    level: step.eventLevel ? levelLabel(t, step.eventLevel) : '',
    kind: step.kind,
  })
}

/**
 * Which step decided the result: the step marked `decisive`, else a floor, else the applied
 * override, else the base level (the calendar decided).
 */
export function decisiveStep(steps: ExplainStepResponse[]): number {
  const marked = steps.findIndex((s) => s.decisive)
  if (marked >= 0) return marked
  for (const kind of ['manager_floor', 'creator_floor', 'override_applied', 'base_level']) {
    const index = steps.findIndex((s) => s.kind === kind)
    if (index >= 0) return index
  }
  return -1
}
