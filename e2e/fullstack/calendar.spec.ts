import { expect, test, type Browser, type Page } from '@playwright/test'
import { linkTo, waitForEmail } from './mailpit'

const password = 'e2e correct horse battery'
const unique = (name: string) =>
  `e2e-${name}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@example.test`
const unsafe = { 'X-Requested-With': 'scal' }

/**
 * A confirmed, signed-in account in its own browser context: registration and login through the
 * api (`page.request` shares the context's cookies), the confirmation link from Mailpit. Collects
 * console errors and CSP violations of the page.
 */
async function account(browser: Browser, name: string) {
  const context = await browser.newContext()
  const page = await context.newPage()
  const errors: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'error' && !/status of 401/.test(message.text())) {
      errors.push(message.text())
    }
  })
  page.on('pageerror', (error) => errors.push(error.message))

  const email = unique(name.toLowerCase())
  const registered = await page.request.post('/api/v1/auth/register', {
    headers: unsafe,
    data: { email, password, displayName: name, locale: 'en', timeZone: 'Europe/Berlin' },
  })
  expect(registered.ok()).toBeTruthy()
  const body = await waitForEmail(page.request, email, /confirm your email/i)
  await page.goto(linkTo(body, '/verify-email'))
  await expect(page.getByRole('heading', { name: 'Email address confirmed' })).toBeVisible()
  const login = await page.request.post('/api/v1/auth/login', {
    headers: unsafe,
    data: { email, password },
  })
  expect(login.ok()).toBeTruthy()
  const me = await (await page.request.get('/api/v1/me')).json()
  return { page, errors, id: me.id as string }
}

/** Wednesday of next week, 10:00–11:30 Berlin wall time (`date` is the view's `?date=`). */
function nextWednesday() {
  const now = new Date()
  const day = now.getUTCDay()
  const wednesday = new Date(now.getTime() + (((3 - day + 7) % 7) + 7) * 86_400_000)
  const date = wednesday.toISOString().slice(0, 10)
  return { date, start: `${date}T10:00:00`, end: `${date}T11:30:00` }
}

async function post(page: Page, path: string, data: unknown) {
  const response = await page.request.post(path, { headers: unsafe, data })
  expect(response.ok(), `${path}: ${response.status()} ${await response.text()}`).toBeTruthy()
  return response.json()
}

// #56: an event created through the api shows in the week view; a user with a free_busy grant
// sees only a "Busy" block without the title anywhere in the DOM; the strict CSP holds.
test('week view shows own events and only busy blocks of a free_busy calendar', async ({
  browser,
}) => {
  const alice = await account(browser, 'Alice')
  const bob = await account(browser, 'Bob')
  const when = nextWednesday()
  const title = `Secret strategy ${Date.now()}`

  // Grants need a shared group (selectable principals): Bob joins Alice's group by link.
  const group = await post(alice.page, '/api/v1/groups', { name: `E2E Cal ${Date.now()}` })
  const invite = await post(alice.page, `/api/v1/groups/${group.id}/invites`, { role: 'member' })
  const token = new URL(invite.url).searchParams.get('token')
  await post(bob.page, '/api/v1/invites/accept', { token })

  const calendar = await post(alice.page, '/api/v1/calendars', {
    name: 'Alice private',
    color: '#0d9488',
    defaultTimeZone: 'Europe/Berlin',
  })
  await post(alice.page, '/api/v1/events', {
    calendarId: calendar.id,
    title,
    location: 'Back office',
    start: { dateTime: when.start, timeZone: 'Europe/Berlin' },
    end: { dateTime: when.end, timeZone: 'Europe/Berlin' },
  })
  await post(alice.page, `/api/v1/calendars/${calendar.id}/grants`, {
    principal: { type: 'user', id: bob.id },
    level: 'free_busy',
  })

  // Alice: the event in the week view, details on click.
  const response = await alice.page.goto(`/calendar?view=week&date=${when.date}`)
  expect(response?.headers()['content-security-policy']).toContain("style-src 'self'")
  const event = alice.page.getByTestId('calendar-event').filter({ hasText: title })
  await expect(event).toBeVisible()
  await expect(event).toContainText('10:00')
  await event.click()
  await expect(alice.page.getByTestId('event-details')).toContainText('Back office')

  // Bob: a busy block in Alice's calendar, no title, not clickable.
  await bob.page.goto(`/calendar?view=week&date=${when.date}`)
  const busy = bob.page.getByTestId('busy-event')
  await expect(busy).toHaveCount(1)
  await expect(busy).toHaveText('Busy')
  await expect(bob.page.getByTestId('sidebar-calendars')).toContainText('Alice private')
  expect(await bob.page.content()).not.toContain(title)
  await busy.click({ force: true })
  await expect(bob.page.getByTestId('event-details')).toHaveCount(0)

  // No CSP violations or script errors on either page (FullCalendar's CSS ships as files).
  expect(alice.errors).toEqual([])
  expect(bob.errors).toEqual([])
})

// #57: a calendar created through the UI, shared through the UI with a user who shares a group
// (selectable principal) — the second user sees it, and a level change reflects in their `myLevel`.
test('create a calendar and share it through the settings page', async ({ browser }) => {
  const alice = await account(browser, 'Alice')
  const bob = await account(browser, 'Bob')
  const name = `Team plans ${Date.now()}`

  const group = await post(alice.page, '/api/v1/groups', { name: `E2E Share ${Date.now()}` })
  const invite = await post(alice.page, `/api/v1/groups/${group.id}/invites`, { role: 'member' })
  const token = new URL(invite.url).searchParams.get('token')
  await post(bob.page, '/api/v1/invites/accept', { token })

  // Alice: create a personal calendar from the sidebar.
  await alice.page.goto('/calendar')
  await alice.page.getByRole('button', { name: 'New calendar' }).click()
  const create = alice.page.getByRole('dialog', { name: 'New calendar' })
  await create.getByLabel('Name').fill(name)
  await create.getByRole('button', { name: 'Create calendar' }).click()
  await expect(
    alice.page.getByRole('heading', { level: 1, name: `${name}: settings` }),
  ).toBeVisible()

  // Share it with Bob (View).
  await alice.page.getByRole('button', { name: 'Share', exact: true }).click()
  const share = alice.page.getByRole('dialog', { name: 'Share the calendar' })
  await share.getByTestId('grant-principal').click()
  await alice.page.getByRole('option', { name: 'Bob' }).click()
  await share.getByTestId('grant-level').click()
  await alice.page.getByRole('option', { name: 'View' }).click()
  await share.getByRole('button', { name: 'Share' }).click()
  await expect(alice.page.getByTestId(`grant-${bob.id}`)).toContainText('Bob')

  // Bob sees the calendar with myLevel read.
  await bob.page.goto('/calendar')
  await expect(bob.page.getByTestId('sidebar-calendars')).toContainText(name)
  const levelOf = async () => {
    const list = await (await bob.page.request.get('/api/v1/calendars?limit=200')).json()
    return list.items.find((c: { name: string }) => c.name === name)?.myLevel
  }
  expect(await levelOf()).toBe('read')

  // Raising to Edit reflects in Bob's myLevel.
  await alice.page.getByTestId(`grant-level-${bob.id}`).click()
  await alice.page.getByRole('option', { name: 'Edit', exact: true }).click()
  await expect(alice.page.getByTestId(`grant-level-${bob.id}`)).toHaveText(/Edit/)
  await expect.poll(levelOf).toBe('edit')

  expect(alice.errors).toEqual([])
  expect(bob.errors).toEqual([])
})
