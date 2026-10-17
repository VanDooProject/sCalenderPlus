import { expect, test, type Page } from '@playwright/test'
import { expectAccessible } from '../support/a11y'
import { calendars, feeds, mock, startSignedOut } from './fixtures'

/**
 * Feed links, share links and event links against the stateful MSW feed mocks
 * (frontend/packages/api-client/src/mocks/feeds.ts, see `feeds` in ./fixtures). Secrets of the
 * mocks start with `mockSecret`; mock state lives in the page: a reload resets it.
 */

/** Records the mutating feed and share link requests as `METHOD path`. */
function recordMutations(page: Page) {
  const requests: string[] = []
  page.on('request', (r) => {
    const url = new URL(r.url())
    if (r.method() !== 'GET' && /feed-tokens|share-links/.test(url.pathname)) {
      requests.push(`${r.method()} ${url.pathname}`)
    }
  })
  return requests
}

const secretInPage = async (page: Page) => (await page.content()).includes('mockSecret')

test('create a feed link, see its address once, then replace and revoke it', async ({
  page,
  context,
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await page.goto('/settings/feeds')
  await expect(page.getByRole('heading', { name: 'Calendar feeds' })).toBeVisible()
  await expect(page.getByTestId('feeds-list')).toContainText('iPhone')
  expect(await secretInPage(page)).toBe(false)
  const requests = recordMutations(page)

  await page.getByRole('button', { name: 'New link' }).click()
  const dialog = page.getByRole('dialog', { name: 'New feed link' })
  await expect(dialog.getByText('All my calendars (recommended)')).toBeVisible()
  await dialog.getByLabel('Name').fill('Work laptop')
  await dialog.getByRole('button', { name: 'Create link' }).click()

  const reveal = page.getByRole('dialog', { name: 'Your feed link' })
  const address = reveal.getByTestId('created-feed-url')
  await expect(address).toHaveValue(/\/ical\/v1\/mockSecret\w+\.ics$/)
  const url = await address.inputValue()
  await expect(reveal.getByRole('link', { name: 'Open in calendar app' })).toHaveAttribute(
    'href',
    url.replace(/^https?:/, 'webcal:'),
  )
  await expect(reveal.getByRole('img', { name: 'QR code of the feed address' })).toBeVisible()
  await reveal.getByRole('button', { name: 'Copy' }).click()
  await expect(reveal.getByRole('button', { name: 'Copied' })).toBeVisible()
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(url)
  await reveal.getByRole('button', { name: 'Done' }).click()

  // Shown once: the list never contains the secret.
  await expect(page.getByTestId('feeds-list')).toContainText('Work laptop')
  expect(await secretInPage(page)).toBe(false)

  // Replace the address.
  await page.getByRole('button', { name: 'New address for Work laptop' }).click()
  await page
    .getByRole('dialog', { name: 'New address for Work laptop?' })
    .getByRole('button', { name: 'New address' })
    .click()
  const renewed = page.getByRole('dialog', { name: 'New address' })
  await expect(renewed.getByTestId('revealed-feed-url')).toHaveValue(/mockSecret/)
  expect(await renewed.getByTestId('revealed-feed-url').inputValue()).not.toBe(url)
  await renewed.getByRole('button', { name: 'Done' }).click()

  // Revoke it.
  await page.getByRole('button', { name: 'Revoke Work laptop' }).click()
  const confirm = page.getByRole('dialog', { name: 'Revoke Work laptop?' })
  await expect(confirm).toContainText('Nobody can recall them sooner')
  await confirm.getByRole('button', { name: 'Revoke' }).click()
  await expect(page.getByTestId('toast')).toContainText('Work laptop revoked.')
  await expect(page.getByTestId('feeds-list')).not.toContainText('Work laptop')

  expect(requests).toEqual([
    'POST /api/v1/feed-tokens',
    expect.stringMatching(/^POST \/api\/v1\/feed-tokens\/[\w-]+\/rotate$/),
    expect.stringMatching(/^DELETE \/api\/v1\/feed-tokens\/[\w-]+$/),
  ])
})

test('a link broken by a password reset asks for a new address', async ({ page }) => {
  await page.goto('/settings/feeds')
  await expect(page.getByTestId('feeds-needs-new')).toContainText('A feed link stopped working')
  await page.getByTestId(`feed-renew-${feeds.resetToken}`).click()
  await expect(
    page.getByRole('dialog', { name: 'New address' }).getByTestId('revealed-feed-url'),
  ).toHaveValue(/mockSecret/)
  await page.getByRole('button', { name: 'Done' }).click()
  await expect(page.getByTestId('feeds-needs-new')).toBeHidden()
})

test('replace all addresses at once', async ({ page }) => {
  await page.goto('/settings/feeds')
  await page.getByRole('button', { name: 'Replace all addresses' }).click()
  await page
    .getByRole('dialog', { name: 'Replace all feed addresses?' })
    .getByRole('button', { name: 'Replace all addresses' })
    .click()
  const reveal = page.getByRole('dialog', { name: 'New addresses' })
  await expect(reveal.getByTestId(`revealed-feed-${feeds.token}`)).toContainText('iPhone')
  await expect(reveal.getByTestId(`revealed-feed-${feeds.resetToken}`)).toBeVisible()
})

test('subscribe to one calendar from the sidebar', async ({ page }) => {
  await page.goto('/calendar')
  const requests = recordMutations(page)
  await page.getByRole('button', { name: 'Options for Mia' }).click()
  await page.getByRole('menuitem', { name: 'Subscribe in calendar app' }).click()
  const dialog = page.getByRole('dialog', { name: 'Subscribe to Mia' })
  await dialog.getByRole('button', { name: 'Create link' }).click()
  await expect(dialog.getByTestId('subscribe-feed-url')).toHaveValue(/mockSecret/)
  await dialog.getByRole('button', { name: 'Done' }).click()
  expect(requests).toEqual(['POST /api/v1/feed-tokens'])

  // A second time: the address can't be shown again, so it offers a new one.
  await page.getByRole('button', { name: 'Options for Mia' }).click()
  await page.getByRole('menuitem', { name: 'Subscribe in calendar app' }).click()
  await expect(dialog.getByTestId('subscribe-existing')).toContainText('already have a link')
  await expect(dialog.getByRole('button', { name: 'New address' })).toBeVisible()
})

test('create and revoke a share link', async ({ page }) => {
  await page.goto(`/calendars/${calendars.lions}/settings`)
  const card = page.getByTestId('share-links')
  await expect(card).toContainText('Club website')
  const requests = recordMutations(page)

  await page.getByRole('button', { name: 'New share link' }).click()
  const dialog = page.getByRole('dialog', { name: 'New share link' })
  await dialog.getByLabel('Label').fill('Parents')
  await dialog.getByRole('button', { name: 'Create link' }).click()
  const created = page.getByRole('dialog', { name: 'Your share link' })
  await expect(created.getByTestId('created-share-link-url')).toHaveValue(
    /\/ical\/v1\/s\/mockSecret\w+\.ics$/,
  )
  await created.getByRole('button', { name: 'Done' }).click()
  await expect(card).toContainText('Parents')
  expect(await secretInPage(page)).toBe(false)

  await page.getByRole('button', { name: 'Revoke Parents' }).click()
  await page
    .getByRole('dialog', { name: 'Revoke Parents?' })
    .getByRole('button', { name: 'Revoke' })
    .click()
  await expect(card).not.toContainText('Parents')
  expect(requests).toEqual([
    `POST /api/v1/calendars/${calendars.lions}/share-links`,
    expect.stringMatching(/^DELETE \/api\/v1\/share-links\/[\w-]+$/),
  ])
})

test('/e/{id} opens the calendar at the event', async ({ page }) => {
  await page.goto(`/e/${feeds.trainingEvent}`)
  await expect(page).toHaveURL(/\/calendar\?date=2026-11-02$/)
  await expect(page.getByTestId('event-details')).toContainText('Training')
})

test('/e/{id}/edit opens the edit dialog', async ({ page }) => {
  await page.goto(`/e/${feeds.trainingEvent}/edit`)
  await expect(page).toHaveURL(/\/calendar\?date=2026-11-02$/)
  await expect(page.getByTestId('event-title')).toHaveValue('Training')
})

test('/e/{id} of an unknown event explains it', async ({ page }) => {
  await page.goto('/e/0192f2c4-0000-7000-8000-00000000dead')
  await expect(page.getByTestId('event-link-gone')).toContainText('Event not available')
  await page.getByRole('link', { name: 'Go to the calendar' }).click()
  await expect(page).toHaveURL(/\/calendar$/)
})

test('/e/{id} signed out leads back after signing in', async ({ page }) => {
  await startSignedOut(page)
  await page.goto(`/e/${feeds.trainingEvent}`)
  await expect(page).toHaveURL(/\/login\?next=/)
  await page.getByLabel('Email address').fill(mock.email)
  await page.getByLabel('Password', { exact: true }).fill(mock.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByTestId('event-details')).toContainText('Training')
})

test('the reset page offers to rotate the feed links', async ({ page }) => {
  await startSignedOut(page)
  await page.goto(`/reset-password?userId=${mock.userId}&token=${mock.linkToken}`)
  const bodies: unknown[] = []
  page.on('request', (r) => {
    if (r.url().endsWith('/api/v1/auth/reset-password')) bodies.push(r.postDataJSON())
  })
  await page.getByTestId('reset-password').fill('a new passphrase')
  await page.getByTestId('reset-confirm').fill('a new passphrase')
  await page.getByRole('checkbox', { name: 'Also rotate my calendar feed links' }).check()
  await page.getByRole('button', { name: 'Set new password' }).click()
  await expect(page.getByTestId('reset-feeds-rotated')).toBeVisible()
  expect(bodies).toEqual([expect.objectContaining({ rotateFeedTokens: true })])
})

for (const scheme of ['light', 'dark'] as const) {
  test.describe(`${scheme} theme`, () => {
    test.use({ colorScheme: scheme })

    test('feed dialogs have no serious a11y violations', async ({ page }) => {
      await page.goto('/settings/feeds')
      await page.getByRole('button', { name: 'New link' }).click()
      await expect(page.getByRole('dialog', { name: 'New feed link' })).toBeVisible()
      await expectAccessible(page, 'create feed dialog')
      await page.getByRole('button', { name: 'Create link' }).click()
      await expect(page.getByTestId('created-feed-url')).toBeVisible()
      await expectAccessible(page, 'feed address dialog')
      await page.getByRole('button', { name: 'Done' }).click()

      await page.goto(`/calendars/${calendars.lions}/settings`)
      await page.getByRole('button', { name: 'New share link' }).click()
      await expect(page.getByRole('dialog', { name: 'New share link' })).toBeVisible()
      await expectAccessible(page, 'create share link dialog')
    })
  })
}
