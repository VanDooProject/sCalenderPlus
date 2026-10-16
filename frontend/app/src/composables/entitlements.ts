import { computed } from 'vue'
import { queryOptions, useQuery } from '@tanstack/vue-query'
import type { components } from '@scalenderplus/api-client'
import { api } from '@/api'
import { call } from '@/lib/apiError'

/** My plan, the end of the reverse trial and the limits (docs/product/plans.md). */
export type Entitlements = components['schemas']['EntitlementsResponse']

export const entitlementsQueryKey = ['entitlements'] as const

export function entitlementsQuery() {
  return queryOptions({
    queryKey: entitlementsQueryKey,
    queryFn: async () => (await call(api.GET('/api/v1/me/entitlements'))).data,
    staleTime: 15 * 60_000,
  })
}

/** `GET /me/entitlements` (signed-in pages only; plan and limits change rarely). */
export function useEntitlements() {
  const query = useQuery(entitlementsQuery())
  return { query, entitlements: computed(() => query.data.value ?? null) }
}

const day = 24 * 60 * 60 * 1000

/**
 * Whole days left of a reverse trial (a part of a day counts as one), or null without a trial, for
 * self-hosted instances and once it has ended.
 */
export function trialDaysLeft(
  entitlements: Pick<Entitlements, 'plan' | 'trialEndsAt'> | null,
  now = Date.now(),
): number | null {
  if (!entitlements?.trialEndsAt || entitlements.plan === 'selfhost') return null
  const end = Date.parse(entitlements.trialEndsAt)
  if (Number.isNaN(end) || end <= now) return null
  return Math.ceil((end - now) / day)
}
