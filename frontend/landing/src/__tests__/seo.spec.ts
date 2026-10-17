import { describe, expect, it } from 'vitest'
import { pageHead } from '@/lib/head'
import { allPaths, pathFor } from '@/lib/routes'
import { alternates, robots, sitemap } from '@/lib/seo'
import { resolveSiteConfig } from '@/lib/siteConfig'

const site = resolveSiteConfig(
  { VITE_SITE_URL: 'https://www.club.example' },
  { id: 'standard', brand: { name: 'Club', hue: 268 } },
)

describe('routes', () => {
  it('puts English at the root and German under /de', () => {
    expect(pathFor('en', 'home')).toBe('/')
    expect(pathFor('de', 'home')).toBe('/de')
    expect(pathFor('en', 'bot')).toBe('/bot')
    expect(pathFor('de', 'terms')).toBe('/de/legal/terms')
  })

  it('prerenders every page in every locale and the 404 page', () => {
    const paths = allPaths()
    expect(paths).toHaveLength(13)
    expect(new Set(paths).size).toBe(13)
    expect(paths).toContain('/404')
    expect(paths).toContain('/de/legal/dpa')
  })
})

describe('hreflang and sitemap', () => {
  it('links each page to its translations with an x-default', () => {
    expect(alternates(site.siteUrl, 'privacy')).toEqual([
      { hreflang: 'en', href: 'https://www.club.example/legal/privacy' },
      { hreflang: 'de', href: 'https://www.club.example/de/legal/privacy' },
      { hreflang: 'x-default', href: 'https://www.club.example/legal/privacy' },
    ])
  })

  it('lists every page with its translations in sitemap.xml', () => {
    const xml = sitemap(site.siteUrl)
    expect(xml.match(/<url>/g)).toHaveLength(12)
    expect(xml).toContain('<loc>https://www.club.example/de/bot</loc>')
    expect(xml).toContain(
      '<xhtml:link rel="alternate" hreflang="de" href="https://www.club.example/de"/>',
    )
    expect(xml).not.toContain('/404')
  })

  it('allows crawling and names the sitemap in robots.txt', () => {
    expect(robots(site.siteUrl)).toBe(
      'User-agent: *\nAllow: /\n\nSitemap: https://www.club.example/sitemap.xml\n',
    )
  })
})

describe('pageHead', () => {
  const texts = { title: 'T', description: 'D', ogAlt: 'A' }

  it('has canonical URL, translations, locale and the locale OG image', () => {
    const head = pageHead(site, 'de', 'bot', texts)
    expect(head.htmlAttrs.lang).toBe('de')
    expect(head.link).toContainEqual({ rel: 'canonical', href: 'https://www.club.example/de/bot' })
    expect(head.link.filter((l) => l.rel === 'alternate')).toHaveLength(3)
    expect(head.meta).toContainEqual({
      property: 'og:image',
      content: 'https://www.club.example/og/de.png',
    })
    expect(head.meta).toContainEqual({ property: 'og:locale', content: 'de_DE' })
    expect(head.meta).not.toContainEqual({ name: 'robots', content: 'noindex' })
  })

  it('keeps the not-found page out of the index', () => {
    const head = pageHead(site, 'en', null, texts)
    expect(head.meta).toContainEqual({ name: 'robots', content: 'noindex' })
    expect(head.link).toEqual([])
  })
})
