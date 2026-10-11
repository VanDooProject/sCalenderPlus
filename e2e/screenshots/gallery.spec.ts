import { fileURLToPath } from 'node:url'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { calendars, groups, mock } from '../mocked/fixtures'

/**
 * Generates the screenshot gallery in docs/screenshots/ (see docs/screenshots.md).
 * Run with `pnpm -C e2e screenshots`; not part of the `mocked`/`fullstack` test projects.
 *
 * The MSW calendar seed is anchored to the current week, so the clock is frozen to a fixed
 * Wednesday: every run shows the same week, the same "now" line and the same busy blocks.
 */
const outDir = fileURLToPath(new URL('../../docs/screenshots/', import.meta.url))
const now = new Date('2026-03-11T10:20:00+01:00')
const mobile = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 } as const

/** Screenshot-only styling: hide the "Environment: mock" badge and the blinking caret. */
const css = `
  [data-testid="environment-badge"] { display: none !important; }
  *, *::before, *::after { caret-color: transparent !important; }
`

test.describe.configure({ mode: 'serial' })

async function prepare(
  page: Page,
  opts: { theme?: 'light' | 'dark'; locale?: 'en' | 'de'; signedOut?: boolean } = {},
) {
  await page.clock.setFixedTime(now)
  await page.addInitScript(
    ({ theme, locale, signedOut, css }) => {
      localStorage.setItem('scal.theme', theme)
      localStorage.setItem('scal.locale', locale)
      if (signedOut) localStorage.setItem('scal.mock.session', 'signed-out')
      document.addEventListener('DOMContentLoaded', () => {
        const style = document.createElement('style')
        style.textContent = css
        document.head.append(style)
      })
    },
    { theme: opts.theme ?? 'light', locale: opts.locale ?? 'en', signedOut: !!opts.signedOut, css },
  )
}

async function settle(page: Page) {
  await page.waitForLoadState('networkidle')
  await page.evaluate(() => document.fonts.ready)
  await expect(page.getByTestId('toast')).toHaveCount(0, { timeout: 15_000 })
  await page.mouse.move(0, 0)
  await page.waitForTimeout(150)
}

async function shot(page: Page, name: string, target?: Locator) {
  await settle(page)
  const path = `${outDir}${name}.png`
  if (target) await target.screenshot({ path, animations: 'disabled' })
  else await page.screenshot({ path, animations: 'disabled' })
}

/** Opens a select and picks an option (retrying the open: the first click can land early). */
async function pick(page: Page, trigger: Locator, name: string) {
  const option = page.getByRole('option', { name, exact: true })
  await expect(async () => {
    if (!(await option.isVisible())) {
      await trigger.focus()
      await page.keyboard.press('Enter')
    }
    await expect(option).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 10_000 })
  await option.click()
}

const event = (page: Page, title: string | RegExp) =>
  page.getByTestId('calendar-event').filter({ hasText: title })

async function openWeek(page: Page) {
  await page.goto('/calendar?view=week')
  await expect(event(page, 'Lunch with Max')).toBeVisible()
  await expect(page.getByTestId('busy-event').first()).toBeVisible()
}

test('login', async ({ page }) => {
  await prepare(page, { signedOut: true })
  await page.goto('/login')
  await expect(page.getByTestId('login-email')).toBeVisible()
  await page.getByTestId('login-email').fill(mock.email)
  await page.getByTestId('login-email').blur()
  await shot(page, 'login')
})

test('calendar week view', async ({ page }) => {
  await prepare(page)
  await openWeek(page)
  await shot(page, 'calendar-week')
})

test('calendar week view dark', async ({ page }) => {
  await prepare(page, { theme: 'dark' })
  await openWeek(page)
  await shot(page, 'calendar-week-dark')
})

test('calendar month view', async ({ page }) => {
  await prepare(page)
  await page.goto('/calendar?view=month')
  await expect(event(page, 'Lunch with Max')).toBeVisible()
  await shot(page, 'calendar-month')
})

test('busy-only blocks next to normal events', async ({ page }) => {
  await prepare(page)
  await openWeek(page)
  // Crop Wednesday to Friday: busy blocks between titled and read-only (lock) events.
  const header = page.getByTestId('calendar')
  const busy = page.getByTestId('busy-event')
  const [head, wed, fri] = await Promise.all([
    header.boundingBox(),
    busy.first().boundingBox(),
    busy.nth(1).boundingBox(),
  ])
  if (!head || !wed || !fri) throw new Error('calendar not rendered')
  const x = wed.x - 3
  const y = head.y
  await settle(page)
  await page.screenshot({
    path: `${outDir}calendar-busy-blocks.png`,
    clip: { x, y, width: fri.x + fri.width + 3 - x, height: head.y + head.height - y },
    animations: 'disabled',
  })
})

test('event details dialog', async ({ page }) => {
  await prepare(page)
  await openWeek(page)
  await event(page, 'Parents’ evening').click()
  const details = page.getByTestId('event-details')
  await expect(page.getByTestId('event-details-level')).toHaveText('You can view this event')
  await shot(page, 'event-details')
  await expect(details).toBeVisible()
})

test('event edit dialog', async ({ page }) => {
  await prepare(page)
  await openWeek(page)
  await event(page, 'League match').click()
  await page.getByTestId('event-details-edit').click()
  await expect(page.getByTestId('event-title')).toHaveValue('League match vs. SV Nord')
  await page.getByTestId('event-more').click()
  await shot(page, 'event-edit')
})

test('event permissions editor', async ({ page }) => {
  await prepare(page)
  await openWeek(page)
  await event(page, 'League match').click()
  await page.getByTestId('event-tab-permissions').click()
  const editor = page.getByTestId('event-permissions')
  await expect(editor.getByTestId('overrides')).toContainText('Max')
  // Unsaved: busy-only for everyone else next to the seeded share with Max (Can edit).
  await editor.getByTestId('preset-busy').click()
  await expect(editor.getByTestId('override-level-everyone')).toContainText('Busy only')
  await shot(page, 'event-permissions')
})

test('event access explainer', async ({ page }) => {
  await prepare(page)
  await openWeek(page)
  await event(page, 'League match').click()
  await page.getByTestId('event-tab-access').click()
  const panel = page.getByTestId('event-explain')
  await expect(panel.getByTestId('explain-result')).toContainText('Your access')
  await pick(page, panel.getByTestId('explain-person'), 'Max')
  await expect(panel.getByTestId('explain-result')).toHaveText('Max: Can edit')
  await shot(page, 'event-explain')
})

test('calendar sharing (grants)', async ({ page }) => {
  await prepare(page)
  await page.goto(`/calendars/${calendars.lions}/settings`)
  const grants = page.getByTestId('grants')
  await expect(grants).toContainText('Eve')
  // A second grant (Max: View) next to the seeded one (Eve: Edit).
  await page.getByRole('button', { name: 'Share', exact: true }).click()
  const add = page.getByRole('dialog', { name: 'Share the calendar' })
  await pick(page, add.getByTestId('grant-principal'), 'Max')
  await pick(page, add.getByTestId('grant-level'), 'View')
  await add.getByRole('button', { name: 'Share' }).click()
  await expect(page.getByTestId(`grant-${groups.max}`)).toContainText('Max')
  await grants.evaluate((el) => el.scrollIntoView({ block: 'center' }))
  await shot(page, 'calendar-sharing')
})

test('role defaults: lowering asks to revoke event shares', async ({ page }) => {
  await prepare(page)
  await page.goto(`/calendars/${calendars.lions}/settings`)
  const form = page.getByTestId('calendar-permissions-form')
  await form.getByTestId('role-default-member').click()
  await page.getByRole('option', { name: 'Free/busy' }).click()
  await expect(
    form.getByRole('checkbox', { name: 'Also revoke individual event shares' }),
  ).toBeChecked()
  await form
    .getByRole('checkbox', { name: 'Also revoke individual event shares' })
    .evaluate((el) => el.scrollIntoView({ block: 'center' }))
  await shot(page, 'calendar-role-defaults')
})

test('group members and roles', async ({ page }) => {
  await prepare(page)
  await page.goto(`/groups/${groups.lions}/members`)
  await expect(page.getByTestId(`member-${groups.vic}`)).toBeVisible()
  await shot(page, 'group-members')
})

test('group invites', async ({ page }) => {
  await prepare(page)
  await page.goto(`/groups/${groups.lions}/invites`)
  await expect(page.getByTestId('invite-link-form')).toBeVisible()
  await shot(page, 'group-invites')
})

test('profile settings dark', async ({ page }) => {
  await prepare(page, { theme: 'dark' })
  await page.goto('/settings/profile')
  await expect(page.getByTestId('profile-email')).toHaveText(mock.email)
  await shot(page, 'profile-dark')
})

test('two-factor setup', async ({ page }) => {
  await prepare(page)
  await page.goto('/settings/security')
  await page.getByTestId('setup-2fa').click()
  await expect(page.getByTestId('shared-key')).toBeVisible()
  await shot(page, 'two-factor-setup')
})

test('German week view', async ({ page }) => {
  await prepare(page, { locale: 'de' })
  await openWeek(page)
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(/^Kalender: /)
  await shot(page, 'calendar-week-de')
})

test.describe('on a phone', () => {
  test.use(mobile)

  test('agenda', async ({ page }) => {
    await prepare(page)
    await page.goto('/calendar')
    await expect(page.getByTestId('calendar-view-agenda')).toHaveAttribute('aria-checked', 'true')
    await expect(page.getByTestId('calendar')).toContainText('Lunch with Max')
    await shot(page, 'mobile-agenda')
  })
})
