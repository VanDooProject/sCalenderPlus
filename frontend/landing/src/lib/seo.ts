import {
  defaultLocale,
  localeTags,
  locales,
  pages,
  pathFor,
  type Locale,
  type Page,
} from './routes'

/** Absolute URL of a path on the landing. */
export const absolute = (siteUrl: string, path: string) => `${siteUrl}${path === '/' ? '/' : path}`

/** The translations of a page (hreflang alternates), `x-default` = the default locale. */
export function alternates(siteUrl: string, page: Page): { hreflang: string; href: string }[] {
  return [
    ...locales.map((locale) => ({
      hreflang: localeTags[locale].hreflang,
      href: absolute(siteUrl, pathFor(locale, page)),
    })),
    { hreflang: 'x-default', href: absolute(siteUrl, pathFor(defaultLocale, page)) },
  ]
}

const xml = (text: string) =>
  text.replace(
    /[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' })[c]!,
  )

/** sitemap.xml: every page in every locale, each with its translations. */
export function sitemap(siteUrl: string): string {
  const entries = locales.flatMap((locale: Locale) =>
    pages.map((page) => {
      const links = alternates(siteUrl, page)
        .map(
          (alt) =>
            `    <xhtml:link rel="alternate" hreflang="${alt.hreflang}" href="${xml(alt.href)}"/>`,
        )
        .join('\n')
      return `  <url>\n    <loc>${xml(absolute(siteUrl, pathFor(locale, page)))}</loc>\n${links}\n  </url>`
    }),
  )
  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">',
    ...entries,
    '</urlset>',
    '',
  ].join('\n')
}

/** robots.txt of the landing: everything may be crawled; points to the sitemap. */
export function robots(siteUrl: string): string {
  return `User-agent: *\nAllow: /\n\nSitemap: ${siteUrl}/sitemap.xml\n`
}
