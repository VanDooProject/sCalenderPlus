import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import type { components } from '@scalenderplus/api-client'
import { api } from '@/api'
import { ApiError, call } from '@/lib/apiError'

/** A personal iCal feed link (never the secret; api.md "Feeds"). */
export type FeedToken = components['schemas']['FeedTokenResponse']
/** A feed link with its secret URLs, returned only by create and rotate. */
export type IssuedFeedToken = components['schemas']['IssuedFeedTokenResponse']
/** An anonymous share link of a calendar (never the secret). */
export type ShareLink = components['schemas']['ShareLinkResponse']
/** A share link with its secret URLs, returned only on create. */
export type IssuedShareLink = components['schemas']['IssuedShareLinkResponse']

/** The secret addresses of a feed, shown once. */
export interface FeedAddress {
  url: string
  webcalUrl: string
}

export const feedScopes = ['aggregate', 'calendar', 'shared_with_me'] as const
export type FeedScope = (typeof feedScopes)[number]

export const labelModes = [
  'prefix_emoji',
  'prefix_emoji_both',
  'suffix_text',
  'description_only',
  'none',
] as const
export type LabelMode = (typeof labelModes)[number]

/** How far back a feed reaches (`includePastDays`, 0–365; the api's default is 90). */
export const pastDaysOptions = [0, 30, 90, 365] as const

/** Expiry choices of a share link (`expiresInDays`; `0` = never). */
export const shareLinkExpiryOptions = [0, 7, 30, 90, 365] as const

export const feedKeys = {
  tokens: ['feed-tokens'] as const,
  shareLinks: (calendarId: string) => ['calendars', calendarId, 'share-links'] as const,
}

const noRetryOnApiError = (count: number, error: unknown) =>
  !(error instanceof ApiError) && count < 2

/** `GET /feed-tokens`: the user's active feed links. */
export function useFeedTokens(enabled: MaybeRefOrGetter<boolean> = true) {
  return useQuery({
    queryKey: feedKeys.tokens,
    queryFn: async () => (await call(api.GET('/api/v1/feed-tokens'))).data.items,
    retry: noRetryOnApiError,
    enabled: computed(() => toValue(enabled)),
  })
}

/** `GET /calendars/{id}/share-links` (calendar `manage`). */
export function useShareLinks(calendarId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => feedKeys.shareLinks(toValue(calendarId))),
    queryFn: async () =>
      (
        await call(
          api.GET('/api/v1/calendars/{id}/share-links', {
            params: { path: { id: toValue(calendarId) } },
          }),
        )
      ).data.items,
    retry: noRetryOnApiError,
  })
}

/** Feed link API calls that keep the cached list current. */
export function useFeedActions() {
  const queryClient = useQueryClient()
  const refresh = () => queryClient.invalidateQueries({ queryKey: feedKeys.tokens })

  async function create(body: components['schemas']['CreateFeedTokenRequest']) {
    const { data } = await call(api.POST('/api/v1/feed-tokens', { body }))
    await refresh()
    return data
  }

  async function rotate(id: string) {
    const { data } = await call(
      api.POST('/api/v1/feed-tokens/{id}/rotate', { params: { path: { id } } }),
    )
    await refresh()
    return data
  }

  async function rotateAll() {
    const { data } = await call(api.POST('/api/v1/feed-tokens/rotate-all'))
    await refresh()
    return data.items
  }

  async function revoke(id: string) {
    try {
      await call(api.DELETE('/api/v1/feed-tokens/{id}', { params: { path: { id } } }))
    } catch (error) {
      // Already gone (another tab): the goal is reached.
      if (!(error instanceof ApiError && error.status === 404)) throw error
    }
    await refresh()
  }

  return { create, rotate, rotateAll, revoke }
}

/** Texts of a feed link: what it shows (scope and calendar) and the optional name. */
export function useFeedLabels() {
  const { t, locale } = useI18n()
  const dateTime = computed(
    () => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }),
  )
  const date = computed(() => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium' }))

  function scopeLabel(token: Pick<FeedToken, 'scope' | 'calendarId'>, calendarName?: string) {
    if (token.scope === 'calendar') {
      return calendarName
        ? t('feeds.scope.calendarNamed', { name: calendarName })
        : t('feeds.scope.calendarGone')
    }
    return token.scope === 'shared_with_me'
      ? t('feeds.scope.shared_with_me')
      : t('feeds.scope.aggregate')
  }

  const formatDateTime = (value: string) => dateTime.value.format(new Date(value))
  const formatDate = (value: string) => date.value.format(new Date(value))

  return { scopeLabel, formatDateTime, formatDate }
}
