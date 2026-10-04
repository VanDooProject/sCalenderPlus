import { expect, test, type Page } from '@playwright/test'
import { groups, mock, startSignedOut } from './fixtures'

const member = (page: Page, userId: string) => page.getByTestId(`member-${userId}`)

async function openMembers(page: Page, groupId: string = groups.lions) {
  await page.goto(`/groups/${groupId}/members`)
  await expect(page.getByTestId('member-list')).toBeVisible()
}

test('create a group and land on its member list', async ({ page }) => {
  await page.goto('/groups')
  await expect(page.getByTestId('group-list')).toContainText('FC Lions')
  await page.getByRole('button', { name: 'New group' }).click()
  const dialog = page.getByRole('dialog', { name: 'Create a group' })
  await dialog.getByLabel('Name').fill('Tennis club')
  await dialog.getByLabel('Description').fill('Saturdays at 10')
  await dialog.getByRole('button', { name: 'Create group' }).click()

  await expect(page).toHaveURL(/\/groups\/[0-9a-f-]+\/members$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Tennis club' })).toBeVisible()
  await expect(page.getByTestId('group-member-count')).toHaveText('1 member')
  await expect(page.getByTestId('sidebar-groups')).toContainText('Tennis club')
})

test('create an invite link with role, expiry and max uses, copy it, see it pending', async ({
  page,
  context,
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await page.goto(`/groups/${groups.lions}/invites`)
  const form = page.getByTestId('invite-link-form')
  // Links carry at most the member role.
  await form.getByTestId('invite-link-role').click()
  await expect(page.getByRole('option')).toHaveText(['Viewer', 'Member'])
  await page.getByRole('option', { name: 'Viewer' }).click()
  await form.getByTestId('invite-link-expiry').click()
  await page.getByRole('option', { name: '3 days' }).click()
  await form.getByLabel('Maximum uses').fill('5')
  await form.getByRole('button', { name: 'Create link' }).click()

  const url = page.getByTestId('invite-link-url')
  await expect(url).toHaveValue(/\/invite\?token=mock-invite-\d+$/)
  await page.getByRole('button', { name: 'Copy link' }).click()
  await expect(page.getByTestId('toast')).toContainText('Link copied.')
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(await url.inputValue())
  await expect(page.getByTestId('invite-list').getByRole('listitem')).toHaveCount(2)
  await expect(page.getByTestId('invite-list')).toContainText('0 of 5 uses')
})

test('an invite link sends a signed-out visitor through the login and joins the group', async ({
  page,
}) => {
  await startSignedOut(page)
  await page.goto(`/invite?token=${groups.inviteToken}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Join Choir' })).toBeVisible()
  await expect(page.getByText('Olga invited you to join as Member.')).toBeVisible()
  await page.getByRole('link', { name: 'Sign in' }).click()

  await expect(page).toHaveURL(/\/login\?next=/)
  await page.getByLabel('Email address').fill(mock.email)
  await page.getByLabel('Password', { exact: true }).fill(mock.password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page).toHaveURL(new RegExp(`/invite\\?token=${groups.inviteToken}$`))
  await page.getByRole('button', { name: 'Accept invitation' }).click()
  await expect(page).toHaveURL(new RegExp(`/groups/${groups.choir}/members$`))
  await expect(page.getByTestId('toast')).toContainText('You joined Choir.')
  await expect(page.getByTestId('sidebar-groups')).toContainText('Choir')
})

test('change a member role (demotion keeps event shares when unchecked)', async ({ page }) => {
  await openMembers(page)
  const requests: string[] = []
  page.on('request', (r) => {
    if (r.method() === 'PATCH')
      requests.push(`${new URL(r.url()).search} ${r.headers()['if-match']}`)
  })
  await page.getByRole('button', { name: 'Actions for Max' }).click()
  await page.getByRole('menuitem', { name: 'Change role' }).click()
  const dialog = page.getByRole('dialog', { name: 'Change role of Max' })
  await dialog.getByTestId('role-select').click()
  await page.getByRole('option', { name: 'Viewer' }).click()
  const revoke = dialog.getByRole('checkbox', {
    name: 'Also revoke individual event shares',
  })
  await expect(revoke).toBeChecked()
  await revoke.click()
  await dialog.getByRole('button', { name: 'Change role' }).click()

  await expect(page.getByTestId('toast')).toContainText('Max is now Viewer.')
  await expect(member(page, groups.max).getByTestId('role-badge')).toHaveText('Viewer')
  expect(requests).toEqual(['?revokeEventShares=false "0003-1"'])
})

test('a member sees actions only on their own row and no invites tab', async ({ page }) => {
  await openMembers(page, groups.bookClub)
  await expect(page.getByRole('button', { name: /Actions for/ })).toHaveCount(1) // only one's own
  await expect(page.getByTestId('tab-invites')).toHaveCount(0)
})

test('remove a member, revoking event shares by default', async ({ page }) => {
  await openMembers(page)
  const deletes: string[] = []
  page.on('request', (r) => {
    if (r.method() === 'DELETE') deletes.push(new URL(r.url()).pathname + new URL(r.url()).search)
  })
  await page.getByRole('button', { name: 'Actions for Vic' }).click()
  await page.getByRole('menuitem', { name: 'Remove from group' }).click()
  const dialog = page.getByRole('dialog', { name: 'Remove Vic?' })
  await expect(
    dialog.getByRole('checkbox', {
      name: 'Also revoke individual event shares',
    }),
  ).toBeChecked()
  await dialog.getByRole('button', { name: 'Remove member' }).click()

  await expect(page.getByTestId('toast')).toContainText('Vic was removed from the group.')
  await expect(member(page, groups.vic)).toHaveCount(0)
  await expect(page.getByTestId('group-member-count')).toHaveText('3 members')
  expect(deletes).toEqual([`/api/v1/groups/${groups.lions}/members/${groups.vic}`])
})

test('leave a group as a member', async ({ page }) => {
  await openMembers(page, groups.bookClub)
  await page.getByRole('button', { name: 'Leave group' }).click()
  const dialog = page.getByRole('dialog', { name: 'Leave Book club?' })
  await dialog.getByRole('button', { name: 'Leave group' }).click()

  await expect(page).toHaveURL(/\/groups$/)
  await expect(page.getByTestId('toast')).toContainText('You left Book club.')
  await expect(page.getByTestId('group-list')).not.toContainText('Book club')
  await expect(page.getByTestId('sidebar-groups')).not.toContainText('Book club')
})

test('the last owner learns why they cannot leave', async ({ page }) => {
  await openMembers(page)
  await expect(member(page, mock.userId)).toContainText(
    'Only owner: make someone else an owner first.',
  )
  await page.getByRole('button', { name: 'Leave group' }).click()
  const dialog = page.getByRole('dialog', { name: 'Leave FC Lions?' })
  await dialog.getByRole('button', { name: 'Leave group' }).click()
  await expect(dialog.getByRole('alert')).toHaveText(
    'You are the only owner. Make another member an owner before you leave, or delete the group.',
  )
  await expect(page).toHaveURL(new RegExp(`/groups/${groups.lions}/members$`))
})
