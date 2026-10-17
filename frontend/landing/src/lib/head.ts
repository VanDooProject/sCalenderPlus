import { localeTags, type Locale, type Page } from './routes'
import { absolute, alternates } from './seo'
import { pathFor } from './routes'
import type { SiteConfig } from './siteConfig'

/** Messages a page's head needs (already translated). */
export interface HeadTexts {
  title: string
  description: string
  ogAlt: string
}

/** OG image size (scripts/render-assets.mjs renders them at this size). */
export const ogImageSize = { width: 1200, height: 630 }

/**
 * Head of a prerendered page: title, description, canonical URL, translations (hreflang), Open
 * Graph and Twitter card. `page` null: the not-found page (no canonical, not indexed).
 */
export function pageHead(site: SiteConfig, locale: Locale, page: Page | null, texts: HeadTexts) {
  const url = page === null ? null : absolute(site.siteUrl, pathFor(locale, page))
  const image = `${site.siteUrl}/og/${locale}.png`
  return {
    htmlAttrs: { lang: localeTags[locale].hreflang },
    title: texts.title,
    meta: [
      { name: 'description', content: texts.description },
      ...(page === null ? [{ name: 'robots', content: 'noindex' }] : []),
      { property: 'og:type', content: 'website' },
      { property: 'og:site_name', content: site.brandName },
      { property: 'og:title', content: texts.title },
      { property: 'og:description', content: texts.description },
      ...(url === null ? [] : [{ property: 'og:url', content: url }]),
      { property: 'og:locale', content: localeTags[locale].og },
      { property: 'og:image', content: image },
      { property: 'og:image:width', content: String(ogImageSize.width) },
      { property: 'og:image:height', content: String(ogImageSize.height) },
      { property: 'og:image:alt', content: texts.ogAlt },
      { name: 'twitter:card', content: 'summary_large_image' },
    ],
    link: [
      ...(url === null ? [] : [{ rel: 'canonical', href: url }]),
      ...(page === null
        ? []
        : alternates(site.siteUrl, page).map((alt) => ({
            rel: 'alternate',
            hreflang: alt.hreflang,
            href: alt.href,
          }))),
    ],
  }
}
