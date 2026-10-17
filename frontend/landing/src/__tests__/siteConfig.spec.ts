import { describe, expect, it } from 'vitest'
import { botToken, resolveSiteConfig } from '@/lib/siteConfig'

const edition = { id: 'standard', brand: { name: 'sCalenderPlus', hue: 268 } }

describe('resolveSiteConfig', () => {
  it('uses the edition defaults and placeholder origins without settings', () => {
    expect(resolveSiteConfig({}, edition)).toEqual({
      edition: 'standard',
      siteUrl: 'https://www.example.com',
      appUrl: 'https://app.example.com',
      brandName: 'sCalenderPlus',
      brandHue: 268,
      supportEmail: null,
      imprintUrl: null,
      privacyUrl: null,
      termsUrl: null,
      dpaUrl: null,
      botUserAgent: 'sCalenderPlusBot/1.0 (+https://www.example.com/bot)',
    })
  })

  it('takes the build settings (trimmed; empty = default)', () => {
    const site = resolveSiteConfig(
      {
        VITE_SITE_URL: ' https://www.club.example/ ',
        VITE_APP_URL: 'https://app.club.example',
        VITE_BRAND_NAME: 'Club Calendar',
        VITE_BRAND_HUE: '150',
        VITE_SUPPORT_EMAIL: 'help@club.example',
        VITE_IMPRINT_URL: 'https://club.example/imprint',
        VITE_PRIVACY_URL: '',
        VITE_DPA_URL: 'https://club.example/dpa.pdf',
      },
      edition,
    )
    expect(site).toMatchObject({
      siteUrl: 'https://www.club.example',
      appUrl: 'https://app.club.example',
      brandName: 'Club Calendar',
      brandHue: 150,
      supportEmail: 'help@club.example',
      imprintUrl: 'https://club.example/imprint',
      privacyUrl: null,
      dpaUrl: 'https://club.example/dpa.pdf',
      botUserAgent: 'sCalenderPlusBot/1.0 (+https://www.club.example/bot)',
    })
  })

  it.each([
    [{ VITE_SITE_URL: 'www.example.com' }, /VITE_SITE_URL/],
    [{ VITE_SITE_URL: 'https://www.example.com/landing' }, /origin without path/],
    [{ VITE_APP_URL: 'javascript:alert(1)' }, /VITE_APP_URL/],
    [{ VITE_APP_URL: 'https://user:pw@app.example.com' }, /without credentials/],
    [{ VITE_BRAND_HUE: '400' }, /VITE_BRAND_HUE/],
    [{ VITE_BRAND_HUE: 'blue' }, /VITE_BRAND_HUE/],
    [{ VITE_SUPPORT_EMAIL: 'not an email' }, /VITE_SUPPORT_EMAIL/],
    [{ VITE_TERMS_URL: 'ftp://example.com/terms' }, /VITE_TERMS_URL/],
    [{ VITE_BOT_USER_AGENT: 'just a bot' }, /VITE_BOT_USER_AGENT/],
  ])('rejects %o', (env, error) => {
    expect(() => resolveSiteConfig(env, edition)).toThrow(error)
  })

  it('names the bot by its user agent product token', () => {
    expect(botToken('sCalenderPlusBot/1.0 (+https://www.example.com/bot)')).toBe('sCalenderPlusBot')
  })
})
