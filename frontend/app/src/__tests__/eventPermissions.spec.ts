import { describe, expect, it } from 'vitest'
import { ApiError } from '@/lib/apiError'
import {
  applyPreset,
  decisiveStep,
  levelLabel,
  overrideLevels,
  principalKey,
  principalLabel,
  requestFromRows,
  rowsFromResponse,
  stepSentence,
  violationMessage,
  violationsByRow,
  violationsOf,
  type OverrideRow,
} from '@/lib/eventPermissions'
import { createAppI18n } from '@/i18n'

const en = createAppI18n('en').global.t as (key: string, named?: Record<string, unknown>) => string
const de = createAppI18n('de').global.t as (key: string, named?: Record<string, unknown>) => string

const vic = { type: 'user', id: 'u-vic' }
const lions = { type: 'group', id: 'g-lions', minRole: 'admin' }

describe('override levels', () => {
  it('are labelled in plain language', () => {
    expect(overrideLevels.map((l) => levelLabel(en, l))).toEqual([
      'Hidden',
      'Busy only',
      'Can view',
      'Can edit',
    ])
    expect(overrideLevels.map((l) => levelLabel(de, l))).toEqual([
      'Verborgen',
      'Nur belegt',
      'Darf ansehen',
      'Darf bearbeiten',
    ])
    expect(levelLabel(en, 'manage')).toBe('Full control')
  })
})

describe('principals', () => {
  it('have stable keys and readable labels', () => {
    expect(principalKey(vic)).toBe('user:u-vic')
    expect(principalKey(lions)).toBe('group:g-lions')
    expect(principalKey({ type: 'everyone' })).toBe('everyone')
    expect(principalLabel(en, vic, 'Vic')).toBe('Vic')
    expect(principalLabel(en, lions, 'FC Lions')).toBe('FC Lions (Admin and above)')
    expect(principalLabel(en, { ...lions, minRole: 'viewer' }, 'FC Lions')).toBe('FC Lions')
    expect(principalLabel(en, { type: 'everyone' }, null)).toBe('Everyone in this calendar')
    expect(principalLabel(en, { type: 'anonymous' }, null)).toBe('Anyone with the link')
  })

  it('round-trip between responses and the PUT body in the api order', () => {
    const rows = rowsFromResponse([
      { principal: { type: 'everyone' }, level: 'free_busy', createdBy: 'x', createdAt: 'y' },
      {
        principal: lions,
        principalName: 'FC Lions',
        level: 'read',
        createdBy: 'x',
        createdAt: 'y',
      },
      { principal: vic, principalName: 'Vic', level: 'none', createdBy: 'x', createdAt: 'y' },
    ])
    expect(rows.map((r) => r.name)).toEqual(['Vic', 'FC Lions', null])
    expect(requestFromRows(rows)).toEqual({
      overrides: [
        { principal: { type: 'user', id: 'u-vic' }, level: 'none' },
        { principal: { type: 'group', id: 'g-lions', minRole: 'admin' }, level: 'read' },
        { principal: { type: 'everyone' }, level: 'free_busy' },
      ],
    })
  })
})

describe('presets', () => {
  const rows: OverrideRow[] = [
    { principal: vic, name: 'Vic', level: 'read' },
    { principal: { type: 'everyone' }, name: null, level: 'read' },
  ]
  it('"Private" hides from everyone, keeping explicit shares', () => {
    expect(applyPreset(rows, 'private')).toEqual([
      rows[0],
      { principal: { type: 'everyone' }, name: null, level: 'none' },
    ])
  })
  it('"Busy only" sets everyone to free_busy', () => {
    expect(applyPreset([], 'busy')).toEqual([
      { principal: { type: 'everyone' }, name: null, level: 'free_busy' },
    ])
  })
})

describe('violations', () => {
  it('are read from 422/403 problems and grouped by row', () => {
    const error = new ApiError(403, {
      code: 'external_sharing_not_allowed',
      violations: [
        { principal: vic, level: 'read', reason: 'external_sharing' },
        { principal: vic, level: 'read', reason: 'user_not_selectable' },
        { principal: { type: 'everyone' }, level: 'edit', reason: 'level_too_high' },
        { nonsense: true },
      ],
    })
    const violations = violationsOf(error)
    expect(violations).toHaveLength(3)
    expect(violationsByRow(violations)).toEqual({
      'user:u-vic': ['external_sharing', 'user_not_selectable'],
      everyone: ['level_too_high'],
    })
  })

  it('are empty for other errors', () => {
    expect(violationsOf(new ApiError(409, { code: 'calendar_frozen', violations: [] }))).toEqual([])
    expect(violationsOf(new Error('x'))).toEqual([])
  })

  it('have sentences, with a fallback for unknown reasons', () => {
    expect(violationMessage(en, 'duplicate_principal')).toBe(
      'This person or group is listed twice.',
    )
    expect(violationMessage(en, 'something_new')).toBe("This entry can't be saved.")
  })
})

describe('explain steps', () => {
  it('become sentences in en and de', () => {
    const step = {
      kind: 'override_applied',
      principal: vic,
      principalName: 'Vic',
      eventLevel: 'read',
    }
    expect(stepSentence(en, step)).toBe(
      'The most specific custom permission (Vic) applies: Can view.',
    )
    expect(stepSentence(de, step)).toBe(
      'Die genaueste eigene Berechtigung (Vic) gilt: Darf ansehen.',
    )
    expect(
      stepSentence(en, {
        kind: 'group_role_default',
        principal: { type: 'group', id: 'g' },
        principalName: 'FC Lions',
        role: 'member',
        calendarLevel: 'contribute',
      }),
    ).toMatch(/^Member of FC Lions as Member: .+ in this calendar\.$/)
    expect(stepSentence(en, { kind: 'creator_floor', eventLevel: 'manage' })).toBe(
      'Created this event, so keeps full control.',
    )
    expect(stepSentence(en, { kind: 'brand_new' })).toBe('Step “brand_new”.')
  })

  it('mark the decisive step', () => {
    expect(
      decisiveStep([{ kind: 'base_level' }, { kind: 'override_applied', decisive: true }]),
    ).toBe(1)
    expect(decisiveStep([{ kind: 'calendar_result' }, { kind: 'manager_floor' }])).toBe(1)
    expect(decisiveStep([{ kind: 'base_level' }, { kind: 'result' }])).toBe(0)
    expect(decisiveStep([{ kind: 'result' }])).toBe(-1)
  })
})
