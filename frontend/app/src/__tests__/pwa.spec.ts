import { afterEach, describe, expect, it, vi } from 'vitest'
import { resetMockAuth } from '@scalenderplus/api-client/mocks'
import caddyfile from '../../../../deploy/caddy/web.Caddyfile?raw'
import manifestTemplate from '../../../../deploy/caddy/manifest.webmanifest.tmpl?raw'
import { applyBrand, defaultBrand } from '@/config'
import { brandThemeColor, networkOnlyNavigations, oklchToHex, webManifest } from '@/lib/webManifest'
import { setUpdateAvailableForTests } from '@/pwa'
import { click, mountApp, queryTestId, resetDom, settle } from './app'
import { useMockApi } from './msw'

useMockApi()

afterEach(() => {
  setUpdateAvailableForTests(false)
  resetDom()
})

describe('web app manifest', () => {
  it('has what Chromium needs to offer the install', () => {
    const manifest = webManifest({ name: 'Club Calendar', hue: 268 })

    expect(manifest).toMatchObject({
      name: 'Club Calendar',
      short_name: 'Club Calendar',
      start_url: '/',
      scope: '/',
      display: 'standalone',
    })
    const png = manifest.icons.filter((icon) => icon.type === 'image/png' && !icon.purpose)
    expect(png.map((icon) => icon.sizes)).toEqual(['192x192', '512x512'])
    expect(manifest.icons.some((icon) => icon.purpose === 'maskable')).toBe(true)
    expect(manifest.theme_color).toMatch(/^#[0-9a-f]{6}$/)
    expect(manifest.background_color).toMatch(/^#[0-9a-f]{6}$/)
  })

  it('takes the theme colour from the brand hue (the palette step 600)', () => {
    expect(oklchToHex(0, 0, 0)).toBe('#000000')
    expect(oklchToHex(1, 0, 0)).toBe('#ffffff')
    // The poly favicon was drawn in the same colour.
    expect(brandThemeColor(330)).toBe('#a624a0')
    expect(brandThemeColor(268)).not.toBe(brandThemeColor(150))
  })

  it('writes name and short name where the web image can replace them', () => {
    const json = JSON.stringify(webManifest({ name: 'sCalenderPlus', hue: 268 }), null, 2)
    // deploy/caddy/manifest.webmanifest.tmpl replaces exactly these two members.
    expect(json).toContain('"name": "sCalenderPlus"')
    expect(json).toContain('"short_name": "sCalenderPlus"')
    expect(manifestTemplate).toContain('regexReplaceAllLiteral `"name": "[^"\\\\]*"`')
    expect(manifestTemplate).toContain('regexReplaceAllLiteral `"short_name": "[^"\\\\]*"`')
    expect(caddyfile).toContain('rewrite * /manifest.webmanifest.tmpl')
    expect(caddyfile).toMatch(/manifest-src 'self'/)
    expect(caddyfile).toMatch(/worker-src 'self'/)
  })
})

describe('service worker', () => {
  const paths = ['/api', '/ical', '/dav', '/.well-known', '/health', '/openapi']
  const denied = (path: string) => networkOnlyNavigations(paths).some((re) => re.test(path))

  it('never answers api, feed or runtime file navigations with the app shell', () => {
    expect(denied('/ical/v1/abc.ics')).toBe(true)
    expect(denied('/ical')).toBe(true)
    expect(denied('/api/v1/me')).toBe(true)
    expect(denied('/api?x=1')).toBe(true)
    expect(denied('/.well-known/caldav')).toBe(true)
    expect(denied('/config.json')).toBe(true)
    expect(denied('/manifest.webmanifest')).toBe(true)
    expect(denied('/healthz')).toBe(true)
  })

  it('answers the app routes with the shell', () => {
    expect(denied('/calendar')).toBe(false)
    expect(denied('/icalendar')).toBe(false)
    expect(denied('/apis')).toBe(false)
    expect(denied('/legal/imprint')).toBe(false)
  })
})

describe('update prompt', () => {
  it('offers a reload when a new version is waiting', async () => {
    resetMockAuth({ signedIn: false })
    const apply = vi.fn(async () => {})
    await mountApp('/login')
    expect(queryTestId('update-prompt')).toBeNull()

    setUpdateAvailableForTests(true, apply)
    await settle()
    await click('update-reload')
    expect(apply).toHaveBeenCalledOnce()
  })

  it('"Later" hides it', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp('/login')
    setUpdateAvailableForTests(true)
    await settle()
    await click('update-later')
    expect(queryTestId('update-prompt')).toBeNull()
  })
})

describe('theme colour at runtime', () => {
  it('follows the configured hue', () => {
    const meta = document.createElement('meta')
    meta.name = 'theme-color'
    meta.content = '#000000'
    document.head.append(meta)
    try {
      applyBrand({ ...defaultBrand, hue: 150 })
      expect(meta.content).toBe(brandThemeColor(150))
    } finally {
      meta.remove()
      document.documentElement.style.removeProperty('--scal-brand-hue')
    }
  })
})
