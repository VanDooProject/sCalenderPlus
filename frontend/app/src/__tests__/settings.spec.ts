import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockAuth, mockCredentials, resetMockAuth } from '@scalenderplus/api-client/mocks'
import { byTestId, click, mountApp, queryTestId, resetDom, settle, submit, type } from './app'
import { useMockApi } from './msw'

const server = useMockApi()

afterEach(() => {
  server.events.removeAllListeners()
  resetDom()
})

const formError = () => queryTestId('form-error')?.textContent?.trim() ?? null

describe('profile', () => {
  it('saves changed fields with the ETag as If-Match', async () => {
    const requests: { ifMatch: string | null; body: unknown }[] = []
    server.events.on('request:start', async ({ request }) => {
      if (request.method === 'PATCH') {
        requests.push({
          ifMatch: request.headers.get('If-Match'),
          body: await request.clone().json(),
        })
      }
    })
    await mountApp('/settings/profile')
    const save = byTestId('profile-save') as HTMLButtonElement
    expect(save.disabled).toBe(true)

    await type('profile-name', 'Mia Muster ')
    expect(save.disabled).toBe(false)
    await submit('profile-form')

    expect(requests).toEqual([{ ifMatch: '"me-1"', body: { displayName: 'Mia Muster' } }])
    expect(mockAuth.user.displayName).toBe('Mia Muster')
    expect(byTestId('toast').textContent).toContain('Profile saved.')
    expect(byTestId('user-menu-trigger').getAttribute('aria-label')).toContain('Mia Muster')
    expect(save.disabled).toBe(true)
  })

  it('reloads after a conflicting change and keeps the edits for another try', async () => {
    await mountApp('/settings/profile')
    // Someone else saved in the meantime: the cached ETag is stale.
    mockAuth.version = 7

    await type('profile-name', 'Mia M.')
    await submit('profile-form')
    expect(formError()).toMatch(/Someone changed this in the meantime/)
    expect((byTestId('profile-name') as HTMLInputElement).value).toBe('Mia M.')

    await submit('profile-form')
    expect(mockAuth.user.displayName).toBe('Mia M.')
  })

  it('picks a time zone by searching', async () => {
    await mountApp('/settings/profile')
    const input = byTestId('time-zone-input') as HTMLInputElement
    expect(input.value).toBe('Europe/Berlin')

    input.focus()
    await type('time-zone-input', 'vienna')
    await settle()
    const options = [...document.querySelectorAll('[role="option"]')].map((o) =>
      o.getAttribute('data-testid'),
    )
    expect(options).toEqual(['time-zone-option-Europe/Vienna'])

    ;(document.querySelector('[role="option"]') as HTMLElement).click()
    await settle()
    await submit('profile-form')
    expect(mockAuth.user.timeZone).toBe('Europe/Vienna')
  })

  it('validates the name on the client', async () => {
    await mountApp('/settings/profile')
    await type('profile-name', '   ')
    await submit('profile-form')
    expect(byTestId('field-error').textContent).toContain('Please fill in this field.')
  })
})

describe('two-factor authentication', () => {
  it('sets up with a QR code, enables with code and password and shows recovery codes', async () => {
    await mountApp('/settings/security')
    expect(byTestId('two-factor-disabled')).toBeTruthy()

    await click('setup-2fa')
    expect(byTestId('qr-code').querySelector('path')?.getAttribute('d')).toMatch(/^M\d/)
    expect(byTestId('shared-key').textContent?.trim()).toBe(
      'JBSW Y3DP EHPK 3PXP JBSW Y3DP EHPK 3PXP',
    )

    await type('enable-code', mockCredentials.totpCode)
    await type('enable-password', 'wrong password')
    await submit('enable-form')
    expect(formError()).toBe('The password or code is incorrect.')

    await type('enable-password', mockCredentials.password)
    await submit('enable-form')

    await vi.waitFor(() =>
      expect(document.querySelectorAll('[data-testid="recovery-code"]')).toHaveLength(10),
    )
    await click('codes-done')
    expect(byTestId('two-factor-enabled')).toBeTruthy()
    expect(byTestId('recovery-left').textContent).toContain('10 recovery codes left')
  })

  it('maps a wrong code to the code field', async () => {
    await mountApp('/settings/security')
    await click('setup-2fa')
    await type('enable-code', '999999')
    await type('enable-password', mockCredentials.password)
    await submit('enable-form')

    expect(byTestId('field-error').textContent).toContain('The code is invalid.')
    expect(formError()).toBe('Please check the highlighted fields.')
  })

  it('needs a verified email before setup', async () => {
    resetMockAuth({ user: { emailVerified: false } })
    await mountApp('/settings/security')
    expect((byTestId('setup-2fa') as HTMLButtonElement).disabled).toBe(true)
  })

  it('regenerates recovery codes and turns 2FA off after re-authentication', async () => {
    resetMockAuth({ user: { twoFactorEnabled: true }, recoveryCodesLeft: 2 })
    await mountApp('/settings/security')
    expect(byTestId('recovery-left').textContent).toContain('2 recovery codes left')

    await click('regenerate-codes')
    await type('reauth-password', mockCredentials.password)
    byTestId('regenerate-dialog')
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }))
    await settle()
    await vi.waitFor(() => expect(queryTestId('codes-dialog')).not.toBeNull())
    await click('codes-done')

    await click('disable-2fa')
    await type('reauth-password', 'wrong password')
    byTestId('disable-dialog')
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }))
    await settle()
    expect(formError()).toBe('The password or code is incorrect.')

    await type('reauth-password', mockCredentials.password)
    byTestId('disable-dialog')
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }))
    await settle()
    await vi.waitFor(() => expect(queryTestId('two-factor-disabled')).not.toBeNull())
    expect(mockAuth.user.twoFactorEnabled).toBe(false)
  })
})
