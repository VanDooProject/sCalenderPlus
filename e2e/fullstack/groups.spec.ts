import { expect, test, type Browser, type APIRequestContext, type Page } from '@playwright/test'
import { linkTo, waitForEmail } from './mailpit'

const password = 'e2e correct horse battery'
const unique = (name: string) =>
  `e2e-${name}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@example.test`

/** Registers through the UI and confirms the address with the link from Mailpit. */
async function registerAndConfirm(
  page: Page,
  request: APIRequestContext,
  name: string,
  email: string,
) {
  await page.getByLabel('Name').fill(name)
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible()

  const body = await waitForEmail(request, email, /confirm your email/i)
  await page.goto(linkTo(body, '/verify-email'))
  await expect(page.getByRole('heading', { name: 'Email address confirmed' })).toBeVisible()
}

async function signIn(page: Page, email: string) {
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
}

async function newPage(browser: Browser) {
  const context = await browser.newContext()
  return context.newPage()
}

// #55 / deferred AC of #36: an email invite → sign-up through the invite link → confirm (Mailpit)
// → joined automatically.
test('email invite: the invitee signs up through the link, confirms and is a member', async ({
  browser,
  request,
}) => {
  const alice = unique('alice')
  const bob = unique('bob')
  const groupName = `E2E Club ${Date.now()}`

  // A: account, group, email invite for B.
  const a = await newPage(browser)
  await a.goto('/register')
  await registerAndConfirm(a, request, 'Alice', alice)
  await a.goto('/login')
  await signIn(a, alice)
  await expect(a).toHaveURL(/\/calendar$/)
  await a.getByTestId('nav-groups').click()
  await a.getByRole('button', { name: 'New group' }).click()
  await a.getByRole('dialog', { name: 'Create a group' }).getByLabel('Name').fill(groupName)
  await a.getByRole('button', { name: 'Create group' }).click()
  await expect(a.getByRole('heading', { level: 1, name: groupName })).toBeVisible()
  await a.getByTestId('tab-invites').click()
  const emailForm = a.getByTestId('invite-email-form')
  await emailForm.getByLabel('Email address').fill(bob)
  await emailForm.getByRole('button', { name: 'Send invitation' }).click()
  await expect(
    a.getByTestId('toast').filter({ hasText: `Invitation sent to ${bob}.` }),
  ).toBeVisible()
  await expect(a.getByTestId('invite-list')).toContainText(bob)

  // B: the invite link shows the group before signing up.
  const invitation = await waitForEmail(request, bob, /invitation to the group/i)
  const b = await newPage(browser)
  await b.goto(linkTo(invitation, '/invite'))
  await expect(b.getByRole('heading', { level: 1, name: `Join ${groupName}` })).toBeVisible()
  await expect(b.getByText('Alice invited you to join as Member.')).toBeVisible()
  await b.getByRole('link', { name: 'Create account' }).click()
  await expect(b).toHaveURL(/\/register\?next=/)
  await registerAndConfirm(b, request, 'Bob', bob)

  // Confirming joined the group: B signs in and sees it.
  await b.getByRole('link', { name: 'Sign in' }).click()
  await signIn(b, bob)
  await expect(b).toHaveURL(/\/calendar$/)
  await expect(b.getByTestId('sidebar-groups')).toContainText(groupName)
  await b.getByTestId('nav-groups').click()
  const card = b.getByTestId('group-list').getByRole('link', { name: new RegExp(groupName) })
  await expect(card).toContainText('Member')
  await expect(card).toContainText('2 members')

  // A sees B as a member; the invite is no longer pending.
  await a.getByTestId('tab-members').click()
  await expect(a.getByTestId('member-list')).toContainText('Bob')
  await a.getByTestId('tab-invites').click()
  await expect(a.getByTestId('invites-empty')).toBeVisible()
})
