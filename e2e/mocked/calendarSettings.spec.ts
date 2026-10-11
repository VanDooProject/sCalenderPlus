import { expect, test, type Page } from '@playwright/test'
import { calendars, groups } from './fixtures'

/**
 * Calendar settings and sharing against the stateful MSW calendar mocks (see `calendars` in
 * ./fixtures). Mock state lives in the page: a reload resets it.
 */

/** Records the mutating calendar requests as `METHOD path?query If-Match`. */
function recordMutations(page: Page) {
  const requests: string[] = []
  page.on('request', (r) => {
    const url = new URL(r.url())
    if (r.method() !== 'GET' && url.pathname.startsWith('/api/v1/calendars')) {
      requests.push(`${r.method()} ${url.pathname}${url.search} ${r.headers()['if-match'] ?? ''}`)
    }
  })
  return requests
}

async function openSettings(page: Page, id: string = calendars.lions) {
  await page.goto(`/calendars/${id}/settings`)
  await expect(page.getByTestId('calendar-settings-title')).toBeVisible()
}

test('create a calendar from the sidebar', async ({ page }) => {
  await page.goto('/calendar')
  await page.getByRole('button', { name: 'New calendar' }).click()
  const dialog = page.getByRole('dialog', { name: 'New calendar' })
  await dialog.getByLabel('Name').fill('Family')
  await dialog.getByTitle('Teal').click()
  await dialog.getByRole('button', { name: 'Create calendar' }).click()

  await expect(page.getByTestId('toast')).toContainText('Calendar Family created.')
  await expect(page.getByRole('heading', { level: 1, name: 'Family: settings' })).toBeVisible()
  await expect(page.getByTestId('sidebar-calendars')).toContainText('Family')
})

test('creating over the plan limit explains it', async ({ page }) => {
  await page.goto('/calendar')
  await page.getByRole('button', { name: 'New calendar' }).click()
  const dialog = page.getByRole('dialog', { name: 'New calendar' })
  await dialog.getByLabel('Name').fill(calendars.planLimitName)
  await dialog.getByRole('button', { name: 'Create calendar' }).click()
  await expect(dialog.getByTestId('form-error')).toContainText('See plans')
})

test('rename and recolor a calendar from the sidebar menu', async ({ page }) => {
  await page.goto('/calendar')
  const requests = recordMutations(page)
  await page.getByRole('button', { name: 'Options for FC Lions – Club' }).click()
  await page.getByRole('menuitem', { name: 'Calendar settings' }).click()
  await expect(page).toHaveURL(new RegExp(`/calendars/${calendars.lions}/settings$`))

  await page.getByTestId('calendar-settings-name').fill('FC Lions – Training')
  await page.getByTitle('Red').click()
  await expect(page.getByRole('radio', { name: 'Red' })).toBeChecked()
  await page.getByTestId('calendar-general-save').click()
  await expect(page.getByTestId('toast')).toContainText('Settings saved.')
  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'FC Lions – Training: settings',
    }),
  ).toBeVisible()
  await expect(page.getByTestId('sidebar-calendars')).toContainText('FC Lions – Training')
  expect(requests).toEqual([`PATCH /api/v1/calendars/${calendars.lions} "cal-0301-1"`])
})

test('add, change and remove grants (revoke checkbox wiring)', async ({ page }) => {
  await openSettings(page)
  const requests = recordMutations(page)

  // Add: Max from FC Lions with "View".
  await page.getByRole('button', { name: 'Share', exact: true }).click()
  const add = page.getByRole('dialog', { name: 'Share the calendar' })
  await add.getByTestId('grant-principal').click()
  await page.getByRole('option', { name: 'Max' }).click()
  await add.getByTestId('grant-level').click()
  await page.getByRole('option', { name: 'View' }).click()
  await add.getByRole('button', { name: 'Share' }).click()
  await expect(page.getByTestId('toast').first()).toContainText('Shared with Max.')
  await expect(page.getByTestId(`grant-${groups.max}`)).toContainText('Max')

  // Raise Max: no question.
  await page.getByTestId(`grant-level-${groups.max}`).click()
  await page.getByRole('option', { name: 'Edit', exact: true }).click()
  await expect(page.getByTestId(`grant-level-${groups.max}`)).toHaveText(/Edit/)

  // Lower Eve: asks, unchecked keeps event shares.
  await page.getByTestId(`grant-level-${calendars.eve}`).click()
  await page.getByRole('option', { name: 'View' }).click()
  const lower = page.getByRole('dialog', {
    name: 'Lower access of Eve to View?',
  })
  const revoke = lower.getByRole('checkbox', {
    name: 'Also revoke individual event shares',
  })
  await expect(revoke).toBeChecked()
  await revoke.click()
  await lower.getByRole('button', { name: 'Lower access' }).click()
  await expect(page.getByTestId(`grant-level-${calendars.eve}`)).toHaveText(/View/)

  // Remove Max, revoking (default).
  await page.getByRole('button', { name: 'Remove access of Max' }).click()
  const remove = page.getByRole('dialog', { name: 'Remove access of Max?' })
  await expect(remove.getByRole('checkbox')).toBeChecked()
  await remove.getByRole('button', { name: 'Remove access' }).click()
  await expect(page.getByTestId(`grant-${groups.max}`)).toHaveCount(0)

  const base = `/api/v1/calendars/${calendars.lions}/grants`
  expect(requests[0]).toBe(`POST ${base} `)
  expect(requests[1]).toMatch(new RegExp(`^PATCH ${base}/[^?]+ "grant-`))
  expect(requests[2]).toMatch(
    new RegExp(`^PATCH ${base}/[^ ]+\\?revokeEventShares=false "grant-0601-1"$`),
  )
  expect(requests[3]).toMatch(new RegExp(`^DELETE ${base}/[^? ]+ "grant-`))
})

test('lowering a role default shows the revoke checkbox', async ({ page }) => {
  await openSettings(page)
  const requests = recordMutations(page)
  const form = page.getByTestId('calendar-permissions-form')
  const revoke = form.getByRole('checkbox', {
    name: 'Also revoke individual event shares',
  })
  await expect(revoke).toHaveCount(0)
  await form.getByTestId('role-default-member').click()
  await page.getByRole('option', { name: 'Edit', exact: true }).click()
  await expect(revoke).toHaveCount(0) // raising does not ask
  await form.getByTestId('role-default-member').click()
  await page.getByRole('option', { name: 'Free/busy' }).click()
  await expect(revoke).toBeChecked()
  await revoke.click()
  await form.getByRole('button', { name: 'Save changes' }).click()
  await expect(page.getByTestId('toast')).toContainText('Settings saved.')
  expect(requests).toEqual([
    `PATCH /api/v1/calendars/${calendars.lions}?revokeEventShares=false "cal-0301-1"`,
  ])
})

test('a frozen calendar keeps cleanup enabled', async ({ page }) => {
  await openSettings(page, calendars.frozen)
  await expect(page.getByTestId('calendar-frozen')).toContainText('frozen')
  await expect(page.getByTestId('calendar-settings-name')).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Share', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Delete calendar' })).toBeEnabled()

  // Lowering offers only lower levels and goes through.
  await page.getByTestId(`grant-level-${groups.adam}`).click()
  await expect(page.getByRole('option', { name: 'Manage' })).toHaveCount(0)
  await page.getByRole('option', { name: 'View' }).click()
  await page
    .getByRole('dialog', { name: 'Lower access of Adam to View?' })
    .getByRole('button', { name: 'Lower access' })
    .click()
  await expect(page.getByTestId(`grant-level-${groups.adam}`)).toHaveText(/View/)

  await page.getByRole('button', { name: 'Remove access of Adam' }).click()
  await page.getByRole('button', { name: 'Remove access', exact: true }).click()
  await expect(page.getByTestId('grants-empty')).toBeVisible()
})

test('delete a calendar after typing its name', async ({ page }) => {
  await openSettings(page, calendars.frozen)
  await page.getByRole('button', { name: 'Delete calendar' }).click()
  const dialog = page.getByRole('dialog', { name: 'Delete Old team?' })
  const confirm = dialog.getByRole('button', { name: 'Delete calendar' })
  await expect(confirm).toBeDisabled()
  await dialog.getByLabel("Type the calendar's name to confirm").fill('Old team')
  await confirm.click()
  await expect(page).toHaveURL(/\/calendar$/)
  await expect(page.getByTestId('toast')).toContainText('Calendar Old team deleted.')
  await expect(page.getByTestId('sidebar-calendars')).not.toContainText('Old team')
})

test('readers get a read-only view', async ({ page }) => {
  await openSettings(page, calendars.bookClub)
  await expect(page.getByTestId('calendar-settings-readonly')).toBeVisible()
  await expect(page.getByTestId('calendar-my-level')).toContainText('View')
  await expect(page.getByTestId('calendar-permissions-form')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Delete calendar' })).toHaveCount(0)
})
