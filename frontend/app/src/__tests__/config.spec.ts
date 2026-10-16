import { afterEach, describe, expect, it, vi } from 'vitest'
import { edition } from '@edition'
import {
  applyBrand,
  defaultBrand,
  defaultConfig,
  loadConfig,
  parseBrand,
  parseConfig,
} from '@/config'

describe('parseConfig', () => {
  it('keeps known string fields', () => {
    expect(parseConfig({ environment: 'staging', extra: 1 })).toEqual({
      environment: 'staging',
      brand: defaultBrand,
    })
  })

  it.each([null, 'x', 42, [], {}, { environment: '' }, { environment: 7 }, { brand: 'x' }])(
    'falls back to defaults for %j',
    (raw) => {
      expect(parseConfig(raw)).toEqual(defaultConfig)
    },
  )
})

describe('parseBrand', () => {
  it("defaults to the edition's brand", () => {
    expect(parseBrand(undefined)).toEqual({
      name: edition.brand.name,
      logoUrl: null,
      hue: edition.brand.hue,
      supportEmail: null,
      imprintUrl: null,
      privacyUrl: null,
      termsUrl: null,
    })
    expect(defaultBrand.name).toBe('sCalenderPlus')
    expect(defaultBrand.hue).toBe(268)
  })

  it('keeps valid values, trimmed', () => {
    expect(
      parseBrand({
        name: '  Club Calendar ',
        logoUrl: 'https://cdn.example.com/logo.svg',
        hue: 145.5,
        supportEmail: ' help@example.com ',
        imprintUrl: '/legal/imprint',
        privacyUrl: 'https://example.com/privacy?lang=en#top',
        termsUrl: 'https://example.com/terms',
        extra: 'ignored',
      }),
    ).toEqual({
      name: 'Club Calendar',
      logoUrl: 'https://cdn.example.com/logo.svg',
      hue: 145.5,
      supportEmail: 'help@example.com',
      imprintUrl: '/legal/imprint',
      privacyUrl: 'https://example.com/privacy?lang=en#top',
      termsUrl: 'https://example.com/terms',
    })
  })

  it.each([0, 360])('accepts the hue bound %d', (hue) => {
    expect(parseBrand({ hue }).hue).toBe(hue)
  })

  it.each([-1, 360.5, Number.NaN, Number.POSITIVE_INFINITY, '120', null, true])(
    'rejects the hue %j',
    (hue) => {
      expect(parseBrand({ hue }).hue).toBe(defaultBrand.hue)
    },
  )

  it.each(['', '   ', 42, null, 'x'.repeat(61), 'Line\nbreak'])('rejects the name %j', (name) => {
    expect(parseBrand({ name }).name).toBe(defaultBrand.name)
  })

  it.each([
    'http://example.com/logo.svg',
    'javascript:alert(1)',
    'data:image/svg+xml,<svg/>',
    '//evil.example/logo.svg',
    'logo.svg',
    'https://user:pass@example.com/logo.svg',
    'https://example.com/a b.svg',
    '/back\\slash',
    'https://',
    7,
  ])('rejects the URL %j', (url) => {
    const brand = parseBrand({ logoUrl: url, imprintUrl: url, privacyUrl: url, termsUrl: url })
    expect([brand.logoUrl, brand.imprintUrl, brand.privacyUrl, brand.termsUrl]).toEqual([
      null,
      null,
      null,
      null,
    ])
  })

  it.each(['no-at-sign', 'a@b', 'a b@example.com', '<a@example.com>', 'a@example.com,b@x.io', 3])(
    'rejects the support email %j',
    (supportEmail) => {
      expect(parseBrand({ supportEmail }).supportEmail).toBeNull()
    },
  )

  it('keeps the valid fields when others are invalid', () => {
    const brand = parseBrand({ name: 'Ok', hue: 999, logoUrl: 'ftp://x' })
    expect(brand).toMatchObject({ name: 'Ok', hue: defaultBrand.hue, logoUrl: null })
  })
})

describe('applyBrand', () => {
  afterEach(() => {
    document.documentElement.style.removeProperty('--scal-brand-hue')
  })

  it('sets the palette hue on <html> and the document title', () => {
    applyBrand({ ...defaultBrand, name: 'Club Calendar', hue: 145 })
    expect(document.documentElement.style.getPropertyValue('--scal-brand-hue')).toBe('145')
    expect(document.title).toBe('Club Calendar')
  })
})

describe('loadConfig', () => {
  it('reads /config.json without caching', async () => {
    const fetchFn = vi
      .fn<typeof fetch>()
      .mockResolvedValue(
        new Response(JSON.stringify({ environment: 'production', brand: { name: 'Acme' } })),
      )

    await expect(loadConfig(fetchFn)).resolves.toEqual({
      environment: 'production',
      brand: { ...defaultBrand, name: 'Acme' },
    })
    expect(fetchFn).toHaveBeenCalledWith(
      '/config.json',
      expect.objectContaining({ cache: 'no-store' }),
    )
  })

  it('uses defaults when the file is missing', async () => {
    const fetchFn = vi.fn<typeof fetch>().mockResolvedValue(new Response('', { status: 404 }))

    await expect(loadConfig(fetchFn)).resolves.toEqual(defaultConfig)
  })

  it('uses defaults when the request fails or the body is not JSON', async () => {
    await expect(
      loadConfig(vi.fn<typeof fetch>().mockRejectedValue(new TypeError())),
    ).resolves.toEqual(defaultConfig)
    await expect(
      loadConfig(vi.fn<typeof fetch>().mockResolvedValue(new Response('<html>'))),
    ).resolves.toEqual(defaultConfig)
  })
})
