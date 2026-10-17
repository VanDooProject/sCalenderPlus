import { afterEach, describe, expect, it } from 'vitest'
import { resetMockAuth } from '@scalenderplus/api-client/mocks'
import { defaultBrand, defaultConfig, type BrandConfig } from '@/config'
import { byTestId, mountApp, queryTestId, resetDom, until } from './app'
import { useMockApi } from './msw'

useMockApi()

afterEach(resetDom)

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? ''

function withBrand(brand: Partial<BrandConfig>) {
  return { config: { ...defaultConfig, brand: { ...defaultBrand, ...brand } } }
}

describe('legal links', () => {
  it.each(['/calendar', '/groups', '/settings/profile', '/help/calendar-apps', '/nowhere'])(
    'are on the app page %s',
    async (path) => {
      await mountApp(path)
      await until(() => queryTestId('legal-links') !== null)
      expect(byTestId('legal-imprint').getAttribute('href')).toBe('/legal/imprint')
      expect(byTestId('legal-privacy').getAttribute('href')).toBe('/legal/privacy')
      expect(byTestId('legal-terms').getAttribute('href')).toBe('/legal/terms')
    },
  )

  it.each(['/login', '/register', '/forgot-password', '/legal/terms'])(
    'are on the public page %s',
    async (path) => {
      resetMockAuth({ signedIn: false })
      await mountApp(path)
      await until(() => queryTestId('legal-links') !== null)
      expect(byTestId('legal-terms').getAttribute('href')).toBe('/legal/terms')
    },
  )

  it('lead to the configured pages when set', async () => {
    await mountApp('/calendar', withBrand({ termsUrl: 'https://example.com/terms' }))
    expect(byTestId('legal-terms').getAttribute('href')).toBe('https://example.com/terms')
    expect(byTestId('legal-imprint').getAttribute('href')).toBe('/legal/imprint')
  })

  it('sign-up names the terms and the privacy policy', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp('/register', withBrand({ privacyUrl: 'https://example.com/privacy' }))
    await until(() => queryTestId('register-legal') !== null)
    expect(text(byTestId('register-legal'))).toBe(
      'By creating an account you agree to the terms of use and acknowledge the privacy policy.',
    )
    expect(byTestId('register-terms').getAttribute('href')).toBe('/legal/terms')
    expect(byTestId('register-privacy').getAttribute('href')).toBe('https://example.com/privacy')
  })
})

describe('legal pages', () => {
  it('show a template marked for the operator when nothing is configured', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp('/legal/imprint')
    await until(() => queryTestId('legal-page-imprint') !== null)

    expect(document.title).toBe('Imprint · sCalenderPlus')
    expect(text(byTestId('legal-template'))).toContain('PUBLIC_BRAND_IMPRINT_URL')
    expect(text(byTestId('legal-page-imprint'))).toContain('[Postal address]')
  })

  it('link to the deployment’s own page when configured', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp(
      '/legal/privacy',
      withBrand({ name: 'Club Calendar', privacyUrl: 'https://example.com/privacy' }),
    )
    await until(() => queryTestId('legal-page-privacy') !== null)

    expect(queryTestId('legal-template')).toBeNull()
    expect(byTestId('legal-external').getAttribute('href')).toBe('https://example.com/privacy')
    expect(text(byTestId('legal-page-privacy'))).toContain('The operator of Club Calendar')
  })

  it('are reachable while signed in', async () => {
    const { router } = await mountApp('/legal/terms', { locale: 'de' })
    await until(() => queryTestId('legal-page-terms') !== null)
    expect(router.currentRoute.value.name).toBe('legal-terms')
    expect(text(byTestId('legal-page-terms'))).toContain('Nutzungsbedingungen')
  })
})

describe('calendar apps guide', () => {
  it('covers Apple, Google, Outlook and Thunderbird with their refresh behaviour', async () => {
    await mountApp('/help/calendar-apps')
    await until(() => queryTestId('help-calendar-apps') !== null)

    for (const app of ['apple', 'google', 'outlook', 'thunderbird']) {
      expect(text(byTestId(`help-${app}`))).toContain('Refresh:')
    }
    expect(text(byTestId('help-google'))).toContain('From URL')
    expect(byTestId('help-link').getAttribute('href')).toBe('/help/calendar-apps')
  })
})
