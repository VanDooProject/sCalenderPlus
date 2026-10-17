import { readonly, ref } from 'vue'
import { isSuppressed, planProblemOf, suppressionKey, type PlanProblem } from '@/lib/planLimits'

export interface PaywallContext {
  /**
   * Whose plan the limit is: `me` when I own the resource (my calendar, a group I'm billing owner
   * of), `other` when only someone else can upgrade (the dialog then says so); unknown: omitted.
   */
  owner?: 'me' | 'other'
}

export interface PaywallOffer extends PaywallContext {
  problem: PlanProblem
}

/** The open upgrade dialog (PaywallDialog.vue in App.vue); one at a time. */
const current = ref<PaywallOffer | null>(null)

export const paywallOffer = readonly(current)

/**
 * Shows the contextual upgrade dialog for a `402 plan_limit_reached`/`feature_not_in_plan`
 * unless the user chose "Not now" for the same limit in the last 14 days. Returns whether the
 * error was a plan error; the caller still shows its own inline message either way, so a
 * suppressed dialog never hides why the action failed.
 */
export function offerUpgrade(error: unknown, context: PaywallContext = {}): boolean {
  const problem = planProblemOf(error)
  if (!problem) return false
  if (!isSuppressed(suppressionKey(problem))) current.value = { problem, ...context }
  return true
}

export function closePaywall(): void {
  current.value = null
}
