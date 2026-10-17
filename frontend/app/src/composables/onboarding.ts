import { computed, reactive, ref, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { useQueries, useQuery } from '@tanstack/vue-query'
import type { EventResponse } from '@scalenderplus/api-client'
import { api } from '@/api'
import { call } from '@/lib/apiError'
import { levelAtLeast } from '@/lib/calendarLevels'
import { asRole, canManageInvites } from '@/lib/groupRoles'
import { useCalendars } from './calendars'
import { useFeedTokens } from './feeds'
import { invitesQuery, useGroups } from './groups'
import { useSession } from './session'

/**
 * The first-run checklist (features.md §5 "Onboarding checklist"). Completion is never stored: each step is
 * derived from what the account already has (there is no endpoint for UI preferences, and the
 * state is the truth anyway). Only hiding it is remembered, per user in `localStorage`.
 */
export const onboardingSteps = ['calendar', 'group', 'feed', 'event'] as const
export type OnboardingStep = (typeof onboardingSteps)[number]

/** Steps that make the checklist complete; `event` is the optional "try the core feature". */
export const requiredSteps: readonly OnboardingStep[] = ['calendar', 'group', 'feed']

/** At most this many groups are asked for their invites (a new account has one or two). */
const inviteLookups = 10

const storageKey = (userId: string) => `scal.onboarding.${userId}`

function readHidden(userId: string): boolean {
  try {
    return localStorage.getItem(storageKey(userId)) === 'hidden'
  } catch {
    return false
  }
}

function writeHidden(userId: string, hidden: boolean) {
  try {
    if (hidden) localStorage.setItem(storageKey(userId), 'hidden')
    else localStorage.removeItem(storageKey(userId))
  } catch {
    // Storage unavailable (private mode): hidden until the next reload only.
  }
}

/** Shared between the checklist and the user menu. */
const state = reactive({
  /** User ids hidden in this page (mirrors `localStorage`, which is not reactive). */
  hidden: {} as Record<string, boolean>,
  /** Opened again from the user menu: shown whatever its progress. */
  reopened: false,
  /** Bumped on each reopen: the checklist moves the focus to its heading. */
  focusRequests: 0,
})

const isHidden = (userId: string) => state.hidden[userId] ?? readHidden(userId)

export function hideOnboarding(userId: string) {
  state.hidden[userId] = true
  state.reopened = false
  writeHidden(userId, true)
}

export function reopenOnboarding(userId: string) {
  state.hidden[userId] = false
  state.reopened = true
  state.focusRequests += 1
  writeHidden(userId, false)
}

export const onboardingFocusRequests = () => state.focusRequests

/** Forgets the page's state (tests: like a reload). */
export function resetOnboardingState() {
  state.hidden = {}
  state.reopened = false
}

/** Events from a week ago to a year ahead (the api allows windows of at most 13 months). */
function eventWindow() {
  const day = 86_400_000
  const now = Date.now()
  const iso = (ms: number) => new Date(ms).toISOString().replace(/\.\d{3}Z$/, 'Z')
  return { from: iso(now - 7 * day), to: iso(now + 365 * day) }
}

/** An event that shows the core feature: a cover title or its own permissions, by the user. */
export function triesCoreFeature(event: EventResponse, userId: string): boolean {
  const mine = event.createdBy?.id === userId || levelAtLeast(event.myLevel, 'edit')
  return mine && (Boolean(event.coverTitle?.trim()) || event.hasOverrides === true)
}

/**
 * Progress of the checklist from existing queries. `active` gates the lookups only the checklist
 * needs (invites, the events window), so accounts that never see it do not pay for them.
 */
export function useOnboarding(active: MaybeRefOrGetter<boolean> = true) {
  const { user } = useSession()
  const userId = computed(() => user.value?.id ?? '')
  const calendars = useCalendars()
  const groups = useGroups()
  const tokens = useFeedTokens()

  const groupWithOthers = computed(() => (groups.data.value ?? []).some((g) => g.memberCount > 1))
  /** Groups whose invites the user may see (admins and owners), the newest first. */
  const managedGroups = computed(() =>
    (groups.data.value ?? [])
      .filter((g) => canManageInvites(asRole(g.myRole)) && !g.frozen)
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt)),
  )
  const invites = useQueries({
    queries: computed(() =>
      managedGroups.value.slice(0, inviteLookups).map((g) => ({
        ...invitesQuery(g.id),
        enabled: toValue(active) && !groupWithOthers.value,
      })),
    ),
  })

  const windowRange = eventWindow()
  const events = useQuery({
    queryKey: ['events', 'onboarding', windowRange] as const,
    queryFn: async () =>
      (await call(api.GET('/api/v1/events', { params: { query: windowRange } }))).data.items,
    enabled: computed(() => toValue(active) && userId.value !== ''),
    staleTime: 60_000,
  })

  const done = computed<Record<OnboardingStep, boolean>>(() => ({
    calendar: (calendars.data.value ?? []).some((c) => levelAtLeast(c.myLevel, 'contribute')),
    group:
      groupWithOthers.value || invites.value.some((q) => (q.data as unknown[] | undefined)?.length),
    feed: (tokens.data.value ?? []).length > 0,
    event: (events.data.value ?? []).some((e) => triesCoreFeature(e, userId.value)),
  }))

  /** Required steps are known (their queries answered), so `complete` can be trusted. */
  const settled = computed(
    () =>
      calendars.isSuccess.value &&
      groups.isSuccess.value &&
      tokens.isSuccess.value &&
      (groupWithOthers.value || invites.value.every((q) => !q.isPending)),
  )
  const failed = computed(
    () => calendars.isError.value || groups.isError.value || tokens.isError.value,
  )
  const doneCount = computed(() => requiredSteps.filter((s) => done.value[s]).length)
  const complete = computed(() => doneCount.value === requiredSteps.length)
  const nextStep = computed(() => onboardingSteps.find((s) => !done.value[s]) ?? null)

  return {
    userId,
    done,
    settled,
    failed,
    doneCount,
    complete,
    nextStep,
    managedGroups,
    hidden: computed(() => userId.value !== '' && isHidden(userId.value)),
    reopened: computed(() => state.reopened),
  }
}

/**
 * Whether the checklist shows: for accounts that are not set up yet (the required steps were not
 * all done when it first loaded), until hidden; or when reopened from the user menu. An account
 * that is set up already when the checklist first loads is hidden for good (existing users).
 */
export function useOnboardingVisibility() {
  const { user } = useSession()
  const startedIncomplete = ref<boolean | null>(null)
  const hidden = computed(() => (user.value ? isHidden(user.value.id) : true))
  const onboarding = useOnboarding(
    () => !hidden.value && (startedIncomplete.value !== false || state.reopened),
  )

  watch(
    () => [onboarding.userId.value, onboarding.settled.value] as const,
    ([id, settled], previous) => {
      if (previous && previous[0] !== id) {
        // Another account signed in on this page: decide again.
        startedIncomplete.value = null
        state.reopened = false
      }
      if (!id || hidden.value || !settled || startedIncomplete.value !== null) return
      startedIncomplete.value = !onboarding.complete.value
      if (!startedIncomplete.value && !state.reopened) hideOnboarding(id)
    },
    { immediate: true },
  )

  const visible = computed(
    () =>
      !hidden.value &&
      !onboarding.failed.value &&
      // Not `settled`: a new group's invites loading must not unmount the checklist (and its dialogs).
      (state.reopened || startedIncomplete.value === true),
  )
  return { ...onboarding, visible }
}
