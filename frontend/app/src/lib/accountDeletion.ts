/** Query key of `GET /me/deletion`. */
export const accountDeletionKey = ['me', 'deletion'] as const

/** The purge date in words, in the viewer's zone (e.g. "31 October 2026"). */
export function formatPurgeDate(purgeAt: string, locale: string): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: 'long' }).format(new Date(purgeAt))
}
