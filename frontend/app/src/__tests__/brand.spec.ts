import { afterEach, describe, expect, it } from 'vitest'
import { resetMockAuth } from '@scalenderplus/api-client/mocks'
import { defaultBrand, defaultConfig, type BrandConfig } from '@/config'
import { byTestId, mountApp, queryTestId, resetDom } from './app'
import { useMockApi } from './msw'

useMockApi()

afterEach(resetDom)

function withBrand(brand: Partial<BrandConfig>) {
  return { config: { ...defaultConfig, brand: { ...defaultBrand, ...brand } } }
}

describe('runtime branding', () => {
  it('shows the edition name and the built-in mark by default', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp('/login')

    expect(byTestId('app-title').textContent?.trim()).toBe('sCalenderPlus')
    expect(document.title).toBe('Sign in · sCalenderPlus')
    expect(queryTestId('brand-logo')).toBeNull()
    expect(queryTestId('legal-links')).toBeNull()
  })

  it('names the app after the configured brand', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp('/login', withBrand({ name: 'Club Calendar' }))

    expect(byTestId('app-title').textContent?.trim()).toBe('Club Calendar')
    expect(document.title).toBe('Sign in · Club Calendar')
  })

  it('shows the configured logo; decorative next to the name, named on its own', async () => {
    await mountApp('/calendar', withBrand({ name: 'Club Calendar', logoUrl: '/brand/logo.svg' }))

    const logos = [...document.querySelectorAll<HTMLImageElement>('[data-testid="brand-logo"]')]
    expect(logos.length).toBeGreaterThan(0)
    expect(logos.every((logo) => logo.getAttribute('src') === '/brand/logo.svg')).toBe(true)
    // The sidebar shows the name next to the logo; the mobile header shows the logo alone.
    expect(logos.map((logo) => logo.alt).sort()).toEqual(['', 'Club Calendar'])
  })

  it('links the configured legal pages and support address', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp(
      '/login',
      withBrand({
        imprintUrl: 'https://example.com/imprint',
        termsUrl: '/terms',
        supportEmail: 'help@example.com',
      }),
    )

    expect(byTestId('legal-imprint').getAttribute('href')).toBe('https://example.com/imprint')
    expect(byTestId('legal-terms').getAttribute('href')).toBe('/terms')
    expect(byTestId('legal-support').getAttribute('href')).toBe('mailto:help@example.com')
    expect(queryTestId('legal-privacy')).toBeNull()
  })
})
